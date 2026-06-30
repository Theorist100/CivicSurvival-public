namespace CivicSurvival.Core.Systems.Base
{
#pragma warning disable CIVIC020 // Phase 1 contract: default closed state is Inactive; AwaitingActState is explicitly assigned by ActGateController.
    public enum ActGateState : byte
    {
        Inactive,
        Active,
        AwaitingActState,
    }
#pragma warning restore CIVIC020
}
