namespace Prism.Worlds.MachineCathedral
{
    /// <summary>Evidence keys. Named constants because the gates and the stations that record
    /// against them must agree exactly — see OrbitalEvidence for the pattern this follows.</summary>
    public static class MachineEvidence
    {
        /// <summary>Any handle turned, pushed or pulled with real, non-trivial displacement.</summary>
        public const string Turn = "turn";

        /// <summary>A configuration with ratio clearly &gt; 1 was both set up AND actually driven —
        /// the effort side moved and the load rose under it. The straddle's force-multiplying half.</summary>
        public const string ForceMultiplying = "ratio.force";
        /// <summary>Ratio clearly &lt; 1, likewise actually driven. The straddle's other half.</summary>
        public const string DistanceMultiplying = "ratio.distance";

        public const string StationLever  = "station.lever";
        public const string StationGear   = "station.gear";
        public const string StationPulley = "station.pulley";

        /// <summary>The learner changed a ratio while the Formalize labels were live, i.e. watched
        /// the mechanical-advantage number respond to their own hand.</summary>
        public const string FormalizeObserve = "formalize.observe";

        public const string ApplySuccess = "apply.success";

        public const string ExplainAttempt = "explain.attempt";
        public const string ExplainCorrect = "explain.correct";

        public const string CreateSuccess = "create.success";

        /// <summary>A hand-thrown switch actually closed under the learner's own hand.</summary>
        public const string SwitchClosed = "logic.switch";
        /// <summary>A relay's contact closed because another circuit energised it — nobody touched it.</summary>
        public const string RelayClosed = "logic.relay";
        public const string LogicAndSeen = "logic.and";
        public const string LogicOrSeen  = "logic.or";
    }
}
