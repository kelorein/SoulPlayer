using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulPlayer.Recorder.Native
{
    internal static class SoulRecorderNativeDiscoveryPolicy
    {
        internal static string ValidatePlayer(
            bool playerExists,
            bool isLocalPlayer,
            bool isFirstPerson)
        {
            if (!playerExists || !isLocalPlayer)
            {
                return "local EFT player is unavailable";
            }
            return isFirstPerson
                ? string.Empty
                : "local player is not in first-person point of view";
        }
    }

    internal sealed class SoulRecorderNativeBoneDescriptor
    {
        internal SoulRecorderNativeBoneDescriptor(string name, string parentName)
        {
            Name = name ?? string.Empty;
            ParentName = parentName ?? string.Empty;
        }

        internal string Name { get; private set; }
        internal string ParentName { get; private set; }
    }

    internal sealed class SoulRecorderNativeArmBones
    {
        internal string Collarbone { get; set; }
        internal string UpperArm { get; set; }
        internal string Forearm { get; set; }
        internal string Wrist { get; set; }
        internal string Palm { get; set; }
        internal IReadOnlyList<string> Fingers { get; set; }

        internal bool IsComplete
        {
            get
            {
                return !string.IsNullOrEmpty(Collarbone) &&
                       !string.IsNullOrEmpty(UpperArm) &&
                       !string.IsNullOrEmpty(Forearm) &&
                       !string.IsNullOrEmpty(Wrist) &&
                       !string.IsNullOrEmpty(Palm);
            }
        }
    }

    internal sealed class SoulRecorderNativeBoneResolution
    {
        internal SoulRecorderNativeArmBones Left { get; set; }
        internal SoulRecorderNativeArmBones Right { get; set; }

        internal bool IsComplete
        {
            get { return Left != null && Right != null && Left.IsComplete && Right.IsComplete; }
        }
    }

    internal static class SoulRecorderNativeBoneRoleResolver
    {
        internal static SoulRecorderNativeBoneResolution Resolve(
            IEnumerable<SoulRecorderNativeBoneDescriptor> source)
        {
            List<SoulRecorderNativeBoneDescriptor> bones = source == null
                ? new List<SoulRecorderNativeBoneDescriptor>()
                : source.Where(value => value != null &&
                    !string.IsNullOrWhiteSpace(value.Name)).ToList();
            Dictionary<string, SoulRecorderNativeBoneDescriptor> byName = bones
                .GroupBy(value => value.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(),
                    StringComparer.Ordinal);

            return new SoulRecorderNativeBoneResolution
            {
                Left = ResolveArm(bones, byName, "l"),
                Right = ResolveArm(bones, byName, "r")
            };
        }

        private static SoulRecorderNativeArmBones ResolveArm(
            List<SoulRecorderNativeBoneDescriptor> bones,
            Dictionary<string, SoulRecorderNativeBoneDescriptor> byName,
            string side)
        {
            SoulRecorderNativeBoneDescriptor collar = Find(
                bones, side, "collarbone", null, byName, false);
            SoulRecorderNativeBoneDescriptor upper = Find(
                bones, side, "upperarm", collar, byName, true);
            SoulRecorderNativeBoneDescriptor forearm = Find(
                bones, side, "forearm1", upper, byName, true) ??
                Find(bones, side, "forearm", upper, byName, true);
            SoulRecorderNativeBoneDescriptor wrist = Find(
                bones, side, "forearm3", forearm, byName, true) ??
                FindDeepest(bones, side, "forearm", forearm, byName);
            SoulRecorderNativeBoneDescriptor palm = Find(
                bones, side, "palm", wrist, byName, true) ??
                Find(bones, side, "hand", wrist, byName, true);

            List<string> fingers = bones
                .Where(value => palm != null && IsDescendantOf(value, palm, byName))
                .Where(value => IsFingerName(Normalize(value.Name)))
                .Select(value => value.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

            return new SoulRecorderNativeArmBones
            {
                Collarbone = Name(collar),
                UpperArm = Name(upper),
                Forearm = Name(forearm),
                Wrist = Name(wrist),
                Palm = Name(palm),
                Fingers = fingers
            };
        }

        private static SoulRecorderNativeBoneDescriptor Find(
            IEnumerable<SoulRecorderNativeBoneDescriptor> bones,
            string side,
            string role,
            SoulRecorderNativeBoneDescriptor ancestor,
            Dictionary<string, SoulRecorderNativeBoneDescriptor> byName,
            bool requireAncestor)
        {
            string expected = "basehuman" + side + role;
            return bones
                .Where(value =>
                {
                    string normalized = Normalize(value.Name);
                    bool roleMatch = normalized == expected ||
                                     (normalized.Contains("human" + side) &&
                                      normalized.Contains(role));
                    if (!roleMatch)
                    {
                        return false;
                    }
                    return !requireAncestor || ancestor == null ||
                           IsDescendantOf(value, ancestor, byName);
                })
                .OrderBy(value => Normalize(value.Name) == expected ? 0 : 1)
                .ThenBy(value => value.Name.Length)
                .FirstOrDefault();
        }

        private static SoulRecorderNativeBoneDescriptor FindDeepest(
            IEnumerable<SoulRecorderNativeBoneDescriptor> bones,
            string side,
            string role,
            SoulRecorderNativeBoneDescriptor ancestor,
            Dictionary<string, SoulRecorderNativeBoneDescriptor> byName)
        {
            return bones
                .Where(value => Normalize(value.Name).Contains("human" + side) &&
                                Normalize(value.Name).Contains(role) &&
                                (ancestor == null || IsDescendantOf(value, ancestor, byName)))
                .OrderByDescending(value => Depth(value, byName))
                .FirstOrDefault();
        }

        private static bool IsDescendantOf(
            SoulRecorderNativeBoneDescriptor candidate,
            SoulRecorderNativeBoneDescriptor ancestor,
            Dictionary<string, SoulRecorderNativeBoneDescriptor> byName)
        {
            if (candidate == null || ancestor == null)
            {
                return false;
            }
            string parent = candidate.ParentName;
            int remaining = byName.Count + 1;
            while (!string.IsNullOrEmpty(parent) && remaining-- > 0)
            {
                if (string.Equals(parent, ancestor.Name, StringComparison.Ordinal))
                {
                    return true;
                }
                SoulRecorderNativeBoneDescriptor next;
                if (!byName.TryGetValue(parent, out next))
                {
                    return false;
                }
                parent = next.ParentName;
            }
            return false;
        }

        private static int Depth(
            SoulRecorderNativeBoneDescriptor value,
            Dictionary<string, SoulRecorderNativeBoneDescriptor> byName)
        {
            int depth = 0;
            string parent = value == null ? string.Empty : value.ParentName;
            while (!string.IsNullOrEmpty(parent) && depth <= byName.Count)
            {
                depth++;
                SoulRecorderNativeBoneDescriptor next;
                if (!byName.TryGetValue(parent, out next))
                {
                    break;
                }
                parent = next.ParentName;
            }
            return depth;
        }

        private static bool IsFingerName(string normalized)
        {
            return normalized.Contains("finger") || normalized.Contains("thumb") ||
                   normalized.Contains("digit") || normalized.Contains("index") ||
                   normalized.Contains("middle") || normalized.Contains("ring") ||
                   normalized.Contains("pinky") || normalized.Contains("little");
        }

        private static string Normalize(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static string Name(SoulRecorderNativeBoneDescriptor value)
        {
            return value == null ? string.Empty : value.Name;
        }
    }
}
