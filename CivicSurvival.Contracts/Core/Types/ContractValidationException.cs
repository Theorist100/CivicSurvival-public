using System;
using System.Runtime.Serialization;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Raised when a generated contract reader rejects wire JSON.
    /// </summary>
    [Serializable]
    public sealed class ContractValidationException : Exception
    {
        public ContractValidationException() { }

        public ContractValidationException(string message) : base(message) { }

        public ContractValidationException(string message, Exception innerException)
            : base(message, innerException) { }

        private ContractValidationException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}
