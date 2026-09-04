namespace SoulPlayer.Recorder
{
    internal enum SoulRecorderCassettePoseAuthority
    {
        None,
        ProceduralFallback,
        CassetteGrip,
        CassetteSlot
    }

    /// <summary>
    /// Pure ownership state used to keep the animated socket hierarchy and the
    /// recorder-only procedural fallback mutually exclusive.
    /// </summary>
    internal sealed class SoulRecorderCassetteTransformAuthority
    {
        internal SoulRecorderCassettePoseAuthority Current { get; private set; }

        internal bool ProceduralTransportAllowed
        {
            get { return Current == SoulRecorderCassettePoseAuthority.ProceduralFallback; }
        }

        internal bool IsHandOwned
        {
            get { return Current == SoulRecorderCassettePoseAuthority.CassetteGrip; }
        }

        internal bool IsRecorderOwned
        {
            get { return Current == SoulRecorderCassettePoseAuthority.CassetteSlot; }
        }

        internal string WriterLabel
        {
            get
            {
                switch (Current)
                {
                    case SoulRecorderCassettePoseAuthority.ProceduralFallback:
                        return "C# procedural fallback";
                    case SoulRecorderCassettePoseAuthority.CassetteGrip:
                        return "Animator hierarchy / CassetteGrip";
                    case SoulRecorderCassettePoseAuthority.CassetteSlot:
                        return "Animator hierarchy / CassetteSlot";
                    default:
                        return "none";
                }
            }
        }

        internal void UseProceduralFallback()
        {
            Current = SoulRecorderCassettePoseAuthority.ProceduralFallback;
        }

        internal void BindToGrip()
        {
            Current = SoulRecorderCassettePoseAuthority.CassetteGrip;
        }

        internal void BindToSlot()
        {
            Current = SoulRecorderCassettePoseAuthority.CassetteSlot;
        }

        internal void Clear()
        {
            Current = SoulRecorderCassettePoseAuthority.None;
        }
    }
}
