using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares that the method must be called only while the caller holds the named lock.
    /// CIVIC114 (lock-asymmetry) treats field accesses inside the method as already lock-protected
    /// because the contract is enforced at every call site, not inside the method body.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CallerHoldsLockAttribute : Attribute
    {
        public CallerHoldsLockAttribute(string lockField)
        {
            LockField = lockField;
        }

        public string LockField { get; }
    }
}
