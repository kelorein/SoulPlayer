using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SoulPlayer.ReleaseAudit
{
    public sealed class ComparisonResult
    {
        public string Status { get; set; }
        public int MethodsCompared { get; set; }
        public int GraphNodesCompared { get; set; }
        public int EmbeddedResourcesCompared { get; set; }
        public string[] RenamedSymbols { get; set; }
        public string[] IgnoredDifferences { get; set; }
    }

    // This is an IL/metadata graph isomorphism check, not a claim that arbitrary
    // different programs can be proven equivalent. Unrecognized changes fail closed.
    public static class SemanticReleaseComparison
    {
        public const string AcceptedHash = "9A5EB4DC26FDD4909D867F0592FB6752418FF6B439C31521728047A90C710C18";
        const string Probe = "SoulPlayer.Utils.RecorderDiagnostics";
        const string Plugin = "SoulPlayer.Plugin";
        const string Profiler = "SoulPlayer.Utils.RecurringWorkProfiler";
        const string ProbeLog = "Recorder discovery probe armed on Ctrl+Shift+F10 (development branch only).";

        sealed class Edge { public string Label; public Node Target; }
        sealed class Node
        {
            public int Id, Color;
            public string Display;
            public bool Generated;
            public readonly StringBuilder Data = new StringBuilder();
            public readonly List<Edge> Out = new List<Edge>(), In = new List<Edge>();
            public void Value(string key, object value)
            {
                string text = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture);
                Data.Append(key.Length).Append(':').Append(key).Append('=').Append(text.Length).Append(':').Append(text).Append(';');
            }
            public void Link(string label, Node target)
            {
                Out.Add(new Edge { Label = label, Target = target });
                target.In.Add(new Edge { Label = label, Target = this });
            }
        }

        sealed class Model
        {
            public readonly List<Node> Nodes = new List<Node>();
            readonly Dictionary<object, Node> symbols = new Dictionary<object, Node>();
            readonly HashSet<object> excluded = new HashSet<object>();
            readonly AssemblyDefinition assembly;
            readonly bool baseline;
            public int MethodCount;
            public Model(AssemblyDefinition a, bool isBaseline)
            {
                assembly = a; baseline = isBaseline;
                if (a.Name.Version.ToString() != (baseline ? "0.9.0.0" : "0.9.1.0")) throw new InvalidOperationException("Unexpected assembly version");
                var types = AllTypes(a.MainModule.Types).ToArray();
                foreach (var t in types)
                {
                    bool probe = t.FullName == Probe || t.FullName.StartsWith(Probe + "/", StringComparison.Ordinal);
                    if (probe && !baseline) throw new InvalidOperationException("Developer probe exists in candidate");
                    if (probe)
                    {
                        excluded.Add(t);
                        foreach (var x in t.Methods) excluded.Add(x);
                        foreach (var x in t.Fields) excluded.Add(x);
                        foreach (var x in t.Properties) excluded.Add(x);
                        continue;
                    }
                    Add(t, "type", t.FullName, GeneratedType(t));
                    foreach (var m in t.Methods)
                        if (baseline && t.FullName == Plugin && (m.Name == "get_RecorderDiagnostics" || m.Name == "set_RecorderDiagnostics")) excluded.Add(m);
                        else { Add(m, "method", m.FullName, GeneratedMember(t, m.Name, m)); MethodCount++; }
                    foreach (var f in t.Fields)
                        if (baseline && t.FullName == Plugin && f.Name == "<RecorderDiagnostics>k__BackingField") excluded.Add(f);
                        else Add(f, "field", f.FullName, GeneratedMember(t, f.Name, f));
                    foreach (var p in t.Properties)
                        if (baseline && t.FullName == Plugin && p.Name == "RecorderDiagnostics") excluded.Add(p);
                        else Add(p, "property", p.FullName, false);
                    foreach (var e in t.Events) Add(e, "event", e.FullName, false);
                }
                Node root = Add(a, "assembly", a.Name.Name, false);
                root.Value("identity", a.Name.Name); root.Value("culture", a.Name.Culture);
                root.Value("flags", a.Name.Attributes); root.Value("public-key", Hex(a.Name.PublicKey));
                root.Value("version", "<approved-release-version>");
                Attributes(root, a.CustomAttributes, "assembly");
                if (a.HasSecurityDeclarations) throw new InvalidOperationException("Unsupported assembly security declarations");
                var module = a.MainModule;
                root.Value("module-name", module.Name); root.Value("runtime", module.RuntimeVersion);
                root.Value("architecture", module.Architecture); root.Value("kind", module.Kind);
                root.Value("module-attributes", module.Attributes); root.Value("characteristics", module.Characteristics);
                foreach (var r in module.AssemblyReferences.OrderBy(x => x.FullName, StringComparer.Ordinal)) root.Value("reference", r.FullName + "|" + r.Attributes + "|" + Hex(r.Hash));
                foreach (var r in module.ModuleReferences.OrderBy(x => x.Name, StringComparer.Ordinal)) root.Value("module-reference", r.Name);
                Attributes(root, module.CustomAttributes, "module");
                foreach (var t in types.Where(x => !excluded.Contains(x))) BuildType(t, root);
                if (module.EntryPoint != null) MethodRef(root, "entry-point", module.EntryPoint);
                CheckInstrumentation(types);
            }
            Node Add(object symbol, string kind, string display, bool generated)
            {
                var n = new Node { Id = Nodes.Count, Display = display, Generated = generated };
                n.Value("kind", kind); Nodes.Add(n); symbols.Add(symbol, n); return n;
            }
            static bool HasGenerated(ICustomAttributeProvider p) { return p.HasCustomAttributes && p.CustomAttributes.Any(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"); }
            static bool GeneratedType(TypeDefinition t) { return HasGenerated(t) && t.Name.StartsWith("<", StringComparison.Ordinal); }
            static bool GeneratedMember(TypeDefinition t, string name, ICustomAttributeProvider p)
            {
                return name.StartsWith("<", StringComparison.Ordinal) && (GeneratedType(t) || HasGenerated(p)) && !name.EndsWith("k__BackingField", StringComparison.Ordinal);
            }
            void BuildType(TypeDefinition t, Node root)
            {
                Node n = symbols[t]; n.Value("name", n.Generated ? "<generated>" : t.Name); n.Value("namespace", t.Namespace);
                n.Value("attributes", t.Attributes); n.Value("packing", t.PackingSize); n.Value("size", t.ClassSize);
                n.Link("owner", t.DeclaringType == null ? root : symbols[t.DeclaringType]);
                TypeRef(n, "base", t.BaseType); Attributes(n, t.CustomAttributes, "type"); Generics(n, t.GenericParameters);
                if (t.HasSecurityDeclarations) throw new InvalidOperationException("Unsupported type security declarations: " + t.FullName);
                foreach (var i in t.Interfaces)
                {
                    Node it = Add(i, "interface", t.FullName + " interface " + i.InterfaceType.FullName, false);
                    n.Link("interface", it); TypeRef(it, "type", i.InterfaceType); Attributes(it, i.CustomAttributes, "interface");
                }
                foreach (var f in t.Fields.Where(x => !excluded.Contains(x)))
                {
                    Node fn = symbols[f]; fn.Link("owner", n); fn.Value("name", fn.Generated ? "<generated>" : f.Name);
                    fn.Value("attributes", f.Attributes); fn.Value("offset", f.Offset); fn.Value("has-constant", f.HasConstant);
                    if (f.HasConstant) Constant(fn, "constant", f.Constant, false);
                    fn.Value("initial-value", Hex(f.InitialValue)); TypeRef(fn, "type", f.FieldType); Attributes(fn, f.CustomAttributes, "field");
                    if (f.HasMarshalInfo) throw new InvalidOperationException("Unsupported field marshalling: " + f.FullName);
                }
                foreach (var m in t.Methods.Where(x => !excluded.Contains(x))) BuildMethod(m, n);
                foreach (var p in t.Properties.Where(x => !excluded.Contains(x)))
                {
                    Node pn = symbols[p]; pn.Link("owner", n); pn.Value("name", p.Name); pn.Value("attributes", p.Attributes);
                    pn.Value("has-this", p.HasThis); TypeRef(pn, "type", p.PropertyType); pn.Value("has-constant", p.HasConstant);
                    if (p.HasConstant) Constant(pn, "constant", p.Constant, false);
                    Parameters(pn, p.Parameters); Attributes(pn, p.CustomAttributes, "property");
                    MethodRef(pn, "get", p.GetMethod); MethodRef(pn, "set", p.SetMethod);
                    foreach (var m in p.OtherMethods) MethodRef(pn, "other", m);
                }
                foreach (var e in t.Events)
                {
                    Node en = symbols[e]; en.Link("owner", n); en.Value("name", e.Name); en.Value("attributes", e.Attributes);
                    TypeRef(en, "type", e.EventType); Attributes(en, e.CustomAttributes, "event");
                    MethodRef(en, "add", e.AddMethod); MethodRef(en, "remove", e.RemoveMethod); MethodRef(en, "invoke", e.InvokeMethod);
                    foreach (var m in e.OtherMethods) MethodRef(en, "other", m);
                }
            }
            void Generics(Node n, IEnumerable<GenericParameter> parameters)
            {
                foreach (var g in parameters)
                {
                    string k = "generic:" + g.Position;
                    n.Value(k + ":name", g.Name); n.Value(k + ":attributes", g.Attributes); Attributes(n, g.CustomAttributes, k);
                    foreach (var c in g.Constraints)
                    {
                        Node cn = Add(c, "generic-constraint", n.Display + " " + k, false);
                        n.Link(k + ":constraint", cn); TypeRef(cn, "type", c);
                    }
                }
            }
            void Parameters(Node n, IEnumerable<ParameterDefinition> parameters)
            {
                foreach (var p in parameters)
                {
                    string k = "parameter:" + p.Index;
                    n.Value(k + ":name", p.Name); n.Value(k + ":attributes", p.Attributes); TypeRef(n, k + ":type", p.ParameterType);
                    n.Value(k + ":has-constant", p.HasConstant); if (p.HasConstant) Constant(n, k + ":constant", p.Constant, false);
                    Attributes(n, p.CustomAttributes, k);
                    if (p.HasMarshalInfo) throw new InvalidOperationException("Unsupported parameter marshalling");
                }
            }
            void BuildMethod(MethodDefinition m, Node owner)
            {
                Node n = symbols[m]; n.Link("owner", owner); n.Value("name", n.Generated ? "<generated>" : m.Name);
                n.Value("attributes", m.Attributes); n.Value("implementation", m.ImplAttributes); n.Value("semantics", m.SemanticsAttributes);
                n.Value("calling-convention", m.CallingConvention); n.Value("has-this", m.HasThis); n.Value("explicit-this", m.ExplicitThis);
                TypeRef(n, "return", m.ReturnType); Parameters(n, m.Parameters); Generics(n, m.GenericParameters);
                n.Value("return-attributes", m.MethodReturnType.Attributes); n.Value("return-has-constant", m.MethodReturnType.HasConstant);
                if (m.MethodReturnType.HasConstant) Constant(n, "return-constant", m.MethodReturnType.Constant, false);
                Attributes(n, m.MethodReturnType.CustomAttributes, "return"); Attributes(n, m.CustomAttributes, "method");
                foreach (var ov in m.Overrides) MethodRef(n, "override", ov);
                if (m.HasSecurityDeclarations || m.MethodReturnType.HasMarshalInfo) throw new InvalidOperationException("Unsupported method metadata: " + m.FullName);
                if (m.HasPInvokeInfo) n.Value("pinvoke", m.PInvokeInfo.Attributes + "|" + m.PInvokeInfo.Module.Name + "|" + m.PInvokeInfo.EntryPoint);
                n.Value("has-body", m.HasBody); if (!m.HasBody) return;
                n.Value("init-locals", m.Body.InitLocals); n.Value("max-stack", m.Body.MaxStackSize);
                foreach (var v in m.Body.Variables) TypeRef(n, "local:" + v.Index, v.VariableType);
                var code = m.Body.Instructions.ToList();
                if (baseline && m.DeclaringType.FullName == Plugin && m.Name == "Awake") RemoveProbeStartup(code);
                var ordinals = code.Select((i, idx) => new { i, idx }).ToDictionary(x => x.i, x => x.idx);
                for (int j = 0; j < code.Count; j++)
                {
                    var ins = code[j]; string k = "il:" + j;
                    // Short/long branch encodings differ only in operand width.
                    string opcode = ins.OpCode.Name;
                    if (ins.OpCode.OperandType == OperandType.ShortInlineBrTarget) opcode = opcode.Substring(0, opcode.Length - 2);
                    n.Value(k + ":opcode", opcode);
                    object o = ins.Operand;
                    if (o is Instruction) n.Value(k + ":target", Target(ordinals, (Instruction)o));
                    else if (o is Instruction[]) n.Value(k + ":targets", string.Join(",", ((Instruction[])o).Select(x => Target(ordinals, x))));
                    else if (o is MethodReference) MethodRef(n, k, (MethodReference)o);
                    else if (o is FieldReference) FieldRef(n, k, (FieldReference)o);
                    else if (o is TypeReference) TypeRef(n, k, (TypeReference)o);
                    else if (o is VariableDefinition) n.Value(k + ":local", ((VariableDefinition)o).Index);
                    else if (o is ParameterDefinition) n.Value(k + ":parameter", ((ParameterDefinition)o).Index);
                    else if (o is CallSite) throw new InvalidOperationException("Unsupported calli operand");
                    else
                    {
                        bool versionText = (m.DeclaringType.FullName == Plugin && m.Name == "Awake") ||
                            (m.DeclaringType.FullName == "SoulPlayer.UI.SoulPlayerWindow" && m.Name == "BuildSidebar");
                        Constant(n, k, o, versionText);
                    }
                }
                for (int j = 0; j < m.Body.ExceptionHandlers.Count; j++)
                {
                    var h = m.Body.ExceptionHandlers[j]; string k = "eh:" + j;
                    n.Value(k + ":kind", h.HandlerType); n.Value(k + ":try-start", Target(ordinals, h.TryStart)); n.Value(k + ":try-end", Target(ordinals, h.TryEnd));
                    n.Value(k + ":handler-start", Target(ordinals, h.HandlerStart)); n.Value(k + ":handler-end", Target(ordinals, h.HandlerEnd));
                    n.Value(k + ":filter", Target(ordinals, h.FilterStart)); TypeRef(n, k + ":catch", h.CatchType);
                }
            }
            static int Target(Dictionary<Instruction, int> ordinals, Instruction i)
            {
                if (i == null) return -1;
                int index; if (!ordinals.TryGetValue(i, out index)) throw new InvalidOperationException("Control flow enters excluded probe instructions");
                return index;
            }
            void TypeRef(Node n, string k, TypeReference t)
            {
                if (t == null) { n.Value(k, null); return; }
                if (t is GenericParameter) { var g = (GenericParameter)t; n.Value(k, "generic:" + g.Type + ":" + g.Position); return; }
                if (t is GenericInstanceType)
                {
                    var g = (GenericInstanceType)t; n.Value(k, "generic-instance"); TypeRef(n, k + ":element", g.ElementType);
                    for (int i = 0; i < g.GenericArguments.Count; i++) TypeRef(n, k + ":arg:" + i, g.GenericArguments[i]); return;
                }
                if (t is TypeSpecification)
                {
                    n.Value(k, t.GetType().Name); TypeRef(n, k + ":element", ((TypeSpecification)t).ElementType);
                    if (t is ArrayType) n.Value(k + ":dimensions", string.Join(";", ((ArrayType)t).Dimensions.Select(x => x.LowerBound + ":" + x.UpperBound)));
                    if (t is RequiredModifierType) TypeRef(n, k + ":modifier", ((RequiredModifierType)t).ModifierType);
                    if (t is OptionalModifierType) TypeRef(n, k + ":modifier", ((OptionalModifierType)t).ModifierType);
                    if (t is FunctionPointerType) throw new InvalidOperationException("Unsupported function-pointer type");
                    return;
                }
                var local = LocalType(t);
                if (local != null) LinkSymbol(n, k, local);
                else n.Value(k, t.FullName + " @ " + (t.Scope == null ? "" : t.Scope.ToString()));
            }
            TypeDefinition LocalType(TypeReference t)
            {
                if (t == null) return null;
                if (t is TypeDefinition) return (TypeDefinition)t;
                while (t is TypeSpecification) t = ((TypeSpecification)t).ElementType;
                if (t.Scope == assembly.MainModule || (t.Scope is AssemblyNameReference && ((AssemblyNameReference)t.Scope).Name == assembly.Name.Name)) return t.Resolve();
                return null;
            }
            void LinkSymbol(Node n, string k, object symbol)
            {
                Node target;
                if (!symbols.TryGetValue(symbol, out target)) throw new InvalidOperationException("Reference to excluded/unknown local symbol: " + symbol);
                n.Link(k, target);
            }
            void MethodRef(Node n, string k, MethodReference m)
            {
                if (m == null) { n.Value(k, null); return; }
                if (m is GenericInstanceMethod)
                {
                    var g = (GenericInstanceMethod)m; n.Value(k, "generic-method"); MethodRef(n, k + ":element", g.ElementMethod);
                    for (int i = 0; i < g.GenericArguments.Count; i++) TypeRef(n, k + ":arg:" + i, g.GenericArguments[i]); return;
                }
                TypeRef(n, k + ":declaring-type", m.DeclaringType);
                if (LocalType(m.DeclaringType) != null) LinkSymbol(n, k + ":definition", m is MethodDefinition ? (MethodDefinition)m : m.Resolve());
                else
                {
                    n.Value(k + ":name", m.Name); n.Value(k + ":convention", m.CallingConvention); n.Value(k + ":has-this", m.HasThis);
                    n.Value(k + ":explicit-this", m.ExplicitThis); n.Value(k + ":generic-count", m.GenericParameters.Count);
                    TypeRef(n, k + ":return", m.ReturnType);
                    for (int i = 0; i < m.Parameters.Count; i++) TypeRef(n, k + ":param:" + i, m.Parameters[i].ParameterType);
                }
            }
            void FieldRef(Node n, string k, FieldReference f)
            {
                TypeRef(n, k + ":declaring-type", f.DeclaringType);
                if (LocalType(f.DeclaringType) != null) LinkSymbol(n, k + ":definition", f is FieldDefinition ? (FieldDefinition)f : f.Resolve());
                else { n.Value(k + ":name", f.Name); TypeRef(n, k + ":type", f.FieldType); }
            }
            void Attributes(Node n, IEnumerable<CustomAttribute> attributes, string slot)
            {
                foreach (var a in attributes)
                {
                    Node an = Add(new object(), "custom-attribute", n.Display + " [" + a.AttributeType.FullName + "]", false);
                    n.Link(slot + ":attribute", an); MethodRef(an, "constructor", a.Constructor);
                    for (int i = 0; i < a.ConstructorArguments.Count; i++)
                    {
                        bool version = (a.AttributeType.FullName == "System.Reflection.AssemblyFileVersionAttribute" && slot == "assembly") ||
                            (a.AttributeType.FullName == "BepInEx.BepInPlugin" && n.Display == Plugin && i == 2);
                        Argument(an, "arg:" + i, a.ConstructorArguments[i], version);
                    }
                    foreach (var f in a.Fields.OrderBy(x => x.Name, StringComparer.Ordinal)) Argument(an, "field:" + f.Name, f.Argument, false);
                    foreach (var p in a.Properties.OrderBy(x => x.Name, StringComparer.Ordinal)) Argument(an, "property:" + p.Name, p.Argument, false);
                }
            }
            void Argument(Node n, string k, CustomAttributeArgument a, bool version)
            {
                TypeRef(n, k + ":type", a.Type);
                if (a.Value is TypeReference) TypeRef(n, k + ":value", (TypeReference)a.Value);
                else if (a.Value is CustomAttributeArgument[]) { var arr = (CustomAttributeArgument[])a.Value; n.Value(k + ":count", arr.Length); for (int i = 0; i < arr.Length; i++) Argument(n, k + ":" + i, arr[i], false); }
                else if (a.Value is CustomAttributeArgument) Argument(n, k + ":boxed", (CustomAttributeArgument)a.Value, false);
                else Constant(n, k + ":value", a.Value, version);
            }
            void Constant(Node n, string k, object value, bool version)
            {
                if (version && value is string)
                {
                    string s = (string)value;
                    string[] approved = { "0.9.0", "0.9.0.0", "SoulPlayer 0.9.0 loaded. Library scan started.", "LOCAL MUSIC  •  0.9.0" };
                    foreach (string old in approved)
                        if (s == (baseline ? old : old.Replace("0.9.0", "0.9.1"))) { value = "<version:" + old + ">"; break; }
                }
                n.Value(k + ":kind", value == null ? "null" : value.GetType().FullName);
                if (value is float) n.Value(k + ":value", Hex(BitConverter.GetBytes((float)value)));
                else if (value is double) n.Value(k + ":value", Hex(BitConverter.GetBytes((double)value)));
                else n.Value(k + ":value", value);
            }
        }

        static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
        {
            foreach (var t in types) { yield return t; foreach (var nested in AllTypes(t.NestedTypes)) yield return nested; }
        }
        static string Hex(byte[] bytes) { return bytes == null ? "" : BitConverter.ToString(bytes).Replace("-", ""); }
        public static string FileHash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return Hex(sha.ComputeHash(stream)); }
        static void RemoveProbeStartup(List<Instruction> code)
        {
            string[] add = { "ldarg.0|", "call|UnityEngine.GameObject UnityEngine.Component::get_gameObject()", "callvirt|!!0 UnityEngine.GameObject::AddComponent<SoulPlayer.Utils.RecorderDiagnostics>()", "call|System.Void SoulPlayer.Plugin::set_RecorderDiagnostics(SoulPlayer.Utils.RecorderDiagnostics)" };
            string[] log = { "call|BepInEx.Logging.ManualLogSource SoulPlayer.Plugin::get_Log()", "ldstr|" + ProbeLog, "callvirt|System.Void BepInEx.Logging.ManualLogSource::LogInfo(System.Object)" };
            foreach (var pattern in new[] { add, log })
            {
                var starts = Enumerable.Range(0, code.Count - pattern.Length + 1).Where(i => pattern.Select((p, j) => p == code[i + j].OpCode.Name + "|" + (code[i + j].Operand == null ? "" : code[i + j].Operand.ToString())).All(x => x)).ToArray();
                if (starts.Length != 1) throw new InvalidOperationException("Expected exactly one approved probe startup sequence");
                code.RemoveRange(starts[0], pattern.Length);
            }
        }
        static void CheckInstrumentation(TypeDefinition[] types)
        {
            var p = types.SingleOrDefault(x => x.FullName == Profiler);
            if (p != null && (p.HasFields || p.Methods.Any(m => !m.HasBody || m.Body.Instructions.Any(i => i.OpCode.Code != Code.Ret)))) throw new InvalidOperationException("Profiler runtime state/code exists");
            foreach (var t in types.Where(x => x.FullName != Probe && !x.FullName.StartsWith(Probe + "/", StringComparison.Ordinal)))
                foreach (var m in t.Methods.Where(x => x.HasBody))
                    if (m.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == Profiler)) throw new InvalidOperationException("Profiler call site: " + m.FullName);
        }

        static int Colorize(Node[] all, Func<Node, string> signature)
        {
            var values = all.Select(signature).ToArray();
            var keys = values.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Select((v, i) => new { v, i }).ToDictionary(x => x.v, x => x.i, StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++) all[i].Color = keys[values[i]];
            return keys.Count;
        }
        static string EdgeColors(List<Edge> edges) { return string.Join(";", edges.Select(e => e.Label.Length + ":" + e.Label + "=" + e.Target.Color).OrderBy(x => x, StringComparer.Ordinal)); }
        static string MappedEdges(Node node, Dictionary<Node, Node> map, bool source)
        {
            return string.Join(";", node.Out.Select(e => e.Label.Length + ":" + e.Label + "=" + (source ? map[e.Target].Id : e.Target.Id)).OrderBy(x => x, StringComparer.Ordinal));
        }
        static bool Compatible(Node a, Node b, Dictionary<Node, Node> map)
        {
            if (a.Color != b.Color || a.Data.ToString() != b.Data.ToString()) return false;
            foreach (var pair in map)
            {
                string aOut = string.Join(";", a.Out.Where(e => e.Target == pair.Key).Select(e => e.Label).OrderBy(x => x, StringComparer.Ordinal));
                string bOut = string.Join(";", b.Out.Where(e => e.Target == pair.Value).Select(e => e.Label).OrderBy(x => x, StringComparer.Ordinal));
                if (aOut != bOut) return false;
                string aIn = string.Join(";", a.In.Where(e => e.Target == pair.Key).Select(e => e.Label).OrderBy(x => x, StringComparer.Ordinal));
                string bIn = string.Join(";", b.In.Where(e => e.Target == pair.Value).Select(e => e.Label).OrderBy(x => x, StringComparer.Ordinal));
                if (aIn != bIn) return false;
            }
            return true;
        }
        static bool CompleteMapping(List<Node> pending, int pos, Dictionary<int, Node[]> groups, Dictionary<Node, Node> map, HashSet<Node> used, ref int attempts)
        {
            if (pos == pending.Count) return map.All(p => MappedEdges(p.Key, map, true) == MappedEdges(p.Value, map, false));
            if (++attempts > 100000) throw new InvalidOperationException("Generated-symbol mapping is ambiguous; manual review required");
            Node a = pending[pos];
            foreach (Node b in groups[a.Color].Where(x => !used.Contains(x)))
            {
                if (!Compatible(a, b, map)) continue;
                map.Add(a, b); used.Add(b);
                if (CompleteMapping(pending, pos + 1, groups, map, used, ref attempts)) return true;
                map.Remove(a); used.Remove(b);
            }
            return false;
        }
        static ComparisonResult Compare(AssemblyDefinition baseline, AssemblyDefinition candidate)
        {
            var left = new Model(baseline, true); var right = new Model(candidate, false);
            Node[] all = left.Nodes.Concat(right.Nodes).ToArray();
            int classes = Colorize(all, n => n.Data.ToString());
            while (true)
            {
                int refined = Colorize(all, n => n.Color + "|out:" + EdgeColors(n.Out) + "|in:" + EdgeColors(n.In));
                if (refined == classes) break;
                classes = refined;
            }
            var l = left.Nodes.GroupBy(n => n.Color).ToDictionary(g => g.Key, g => g.ToArray());
            var r = right.Nodes.GroupBy(n => n.Color).ToDictionary(g => g.Key, g => g.ToArray());
            var bad = l.Keys.Union(r.Keys).Where(k => !l.ContainsKey(k) || !r.ContainsKey(k) || l[k].Length != r[k].Length).ToArray();
            if (bad.Length != 0)
            {
                var names = bad.SelectMany(k => l.ContainsKey(k) ? l[k] : r[k]).Select(n => n.Display).Distinct().Take(12);
                throw new InvalidOperationException("Runtime IL/metadata graph differs: " + string.Join("; ", names));
            }
            var map = new Dictionary<Node, Node>(); var used = new HashSet<Node>();
            foreach (int k in l.Keys.Where(k => l[k].Length == 1)) { map.Add(l[k][0], r[k][0]); used.Add(r[k][0]); }
            var pending = left.Nodes.Where(n => !map.ContainsKey(n)).OrderBy(n => r[n.Color].Length).ToList(); int attempts = 0;
            if (!CompleteMapping(pending, 0, r, map, used, ref attempts)) throw new InvalidOperationException("Generated-symbol reference graph is not isomorphic");
            int resources = CompareResources(baseline, candidate);
            return new ComparisonResult {
                Status = "PASS", MethodsCompared = left.MethodCount, GraphNodesCompared = left.Nodes.Count, EmbeddedResourcesCompared = resources,
                RenamedSymbols = map.Where(p => p.Key.Generated && p.Key.Display != p.Value.Display).Select(p => p.Key.Display + " => " + p.Value.Display).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                IgnoredDifferences = new[] {
                    "Compiler-generated symbol names only after bijective IL/metadata/reference graph matching; declarations and graph edges remain checked.",
                    "Metadata token values, declaration-table order, MVID, PE timestamp/checksum/layout, debug/PDB sequence points, and instruction byte offsets; branches and exception regions compare instruction ordinals.",
                    "Short versus long branch operand encoding only; targets and opcode semantics remain checked.",
                    "Assembly 0.9.0.0 -> 0.9.1.0, AssemblyFileVersion, BepInPlugin version, exact startup version log, and exact UI version label. No general string or numeric constant replacement.",
                    "Baseline RecorderDiagnostics type/nested types and Plugin probe property/accessors/backing field; exactly four AddComponent/store instructions and three probe-announcement instructions removed from Awake. All remaining references and startup instructions checked.",
                    "No embedded asset/resource payload changes permitted. No Harmony registration/attribute, configuration default, production method body, or control-flow changes permitted."
                }
            };
        }
        static int CompareResources(AssemblyDefinition a, AssemblyDefinition b)
        {
            if (a.MainModule.Resources.Count != b.MainModule.Resources.Count) throw new InvalidOperationException("Resource count differs");
            foreach (var ra in a.MainModule.Resources)
            {
                var rb = b.MainModule.Resources.SingleOrDefault(x => x.Name == ra.Name);
                if (rb == null || rb.Attributes != ra.Attributes || !(ra is EmbeddedResource) || !(rb is EmbeddedResource) || !((EmbeddedResource)ra).GetResourceData().SequenceEqual(((EmbeddedResource)rb).GetResourceData())) throw new InvalidOperationException("Resource differs: " + ra.Name);
            }
            return a.MainModule.Resources.Count;
        }
        public static ComparisonResult Run(string baseline, string candidate)
        {
            if (FileHash(baseline) != AcceptedHash) throw new InvalidOperationException("Baseline hash is not the EFT-tested C18 DLL");
            using (var a = AssemblyDefinition.ReadAssembly(baseline)) using (var b = AssemblyDefinition.ReadAssembly(candidate)) return Compare(a, b);
        }
        public static string[] SelfTest(string baseline, string candidate)
        {
            if (FileHash(baseline) != AcceptedHash) throw new InvalidOperationException("Self-test baseline hash mismatch");
            var passed = new List<string>();
            Action<string, bool, Action<AssemblyDefinition>> test = (name, expectedPass, mutate) => {
                using (var a = AssemblyDefinition.ReadAssembly(baseline)) using (var b = AssemblyDefinition.ReadAssembly(candidate))
                {
                    mutate(b); bool actualPass = false;
                    try { Compare(a, b); actualPass = true; } catch (InvalidOperationException) { }
                    if (actualPass != expectedPass) throw new InvalidOperationException("Comparator self-test failed: " + name);
                    passed.Add(name + ": PASS");
                }
            };
            Func<AssemblyDefinition, string, string, MethodDefinition> method = (a, type, name) => AllTypes(a.MainModule.Types).Single(t => t.FullName == type).Methods.First(m => m.Name == name && m.HasBody);
            test("accepted candidate", true, a => { });
            test("generated lambda/closure/cache renumbering", true, a => {
                var p = AllTypes(a.MainModule.Types).Single(t => t.FullName == Plugin).NestedTypes.Single(t => t.Name == "<>c");
                p.Name = "<>c__renumbered900";
                foreach (var m in p.Methods.Where(m => m.Name.StartsWith("<Awake>"))) m.Name = "<Awake>b__900_" + (900 - m.MetadataToken.RID);
                foreach (var f in p.Fields.Where(f => f.Name.StartsWith("<>9__"))) f.Name = "<>9__901_" + (900 - f.MetadataToken.RID);
            });
            test("metadata declaration reordering and MVID", true, a => {
                foreach (var t in AllTypes(a.MainModule.Types))
                {
                    var methods = t.Methods.Reverse().ToArray(); t.Methods.Clear(); foreach (var m in methods) t.Methods.Add(m);
                    var fields = t.Fields.Reverse().ToArray(); t.Fields.Clear(); foreach (var f in fields) t.Fields.Add(f);
                }
                a.MainModule.Mvid = Guid.NewGuid();
            });
            test("startup Harmony registration string change rejected", false, a => {
                method(a, Plugin, "Awake").Body.Instructions.First(i => i.OpCode.Code == Code.Ldstr && (string)i.Operand == "raid deployment").Operand = "changed deployment registration";
            });
            test("production startup instruction change rejected", false, a => {
                var i = method(a, Plugin, "Awake").Body.Instructions.First(x => x.OpCode.Code == Code.Callvirt);
                i.OpCode = OpCodes.Call;
            });
            test("Harmony attribute removal rejected", false, a => {
                method(a, "SoulPlayer.Patches.RaidDeploymentPatch", "PatchPrefix").CustomAttributes.Clear();
            });
            test("recorder default hotkey change rejected", false, a => {
                var m = method(a, "SoulPlayer.Configuration.SoulPlayerSettings", ".ctor");
                var i = m.Body.Instructions.First(x => x.Operand is sbyte && (sbyte)x.Operand == 109);
                i.Operand = (sbyte)110;
            });
            test("config setting-name change rejected", false, a => {
                method(a, "SoulPlayer.Configuration.SoulPlayerSettings", ".ctor").Body.Instructions.First(i => i.OpCode.Code == Code.Ldstr && (string)i.Operand == "Mini-player position").Operand = "Changed setting key";
            });
            test("mini-player layout constant change rejected", false, a => {
                var i = method(a, "SoulPlayer.UI.SoulMiniPlayerLayout", "Calculate").Body.Instructions.First(x => x.OpCode.Code == Code.Ldc_R4);
                i.Operand = (float)i.Operand + 1f;
            });
            test("recorder input branch retarget rejected", false, a => {
                var m = method(a, "SoulPlayer.Recorder.SoulRecorderInput", "Poll");
                var i = m.Body.Instructions.First(x => x.Operand is Instruction);
                i.Operand = ((Instruction)i.Operand == m.Body.Instructions[0]) ? m.Body.Instructions[1] : m.Body.Instructions[0];
            });
            test("exact-resume instruction change rejected", false, a => {
                method(a, "SoulPlayer.Audio.MainPlaybackSnapshot", "ResumeSamples").Body.Instructions[0].OpCode = OpCodes.Nop;
            });
            test("readiness performance branch change rejected", false, a => {
                var m = method(a, "SoulPlayer.Audio.RaidReadinessPollGate", "ShouldInspect");
                var i = m.Body.Instructions.First(x => x.Operand is Instruction); i.Operand = m.Body.Instructions[0];
            });
            test("public API visibility change rejected", false, a => {
                var t = AllTypes(a.MainModule.Types).Single(x => x.FullName == "SoulPlayer.UI.SoulMiniPlayerLayout");
                t.Attributes = (t.Attributes & ~TypeAttributes.VisibilityMask) | TypeAttributes.NotPublic;
            });
            test("embedded anchor modification rejected", false, a => {
                var resource = (EmbeddedResource)a.MainModule.Resources.First(x => x.Name.IndexOf("anchor", StringComparison.OrdinalIgnoreCase) >= 0);
                byte[] bytes = resource.GetResourceData(); bytes[0] ^= 1;
                int index = a.MainModule.Resources.IndexOf(resource);
                a.MainModule.Resources[index] = new EmbeddedResource(resource.Name, resource.Attributes, bytes);
            });
            test("generated lambda body change rejected despite renaming", false, a => {
                var t = AllTypes(a.MainModule.Types).Single(x => x.FullName == Plugin).NestedTypes.Single(x => x.Name == "<>c");
                var m = t.Methods.First(x => x.Name.StartsWith("<Awake>")); m.Name = "<Awake>b__999_999";
                m.Body.Instructions[0].OpCode = OpCodes.Nop;
            });
            return passed.ToArray();
        }
    }
}
