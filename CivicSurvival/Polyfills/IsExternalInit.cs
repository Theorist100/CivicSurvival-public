// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill for C# 9 init-only properties on .NET Framework 4.8.
    /// Required for record types with init properties.
    /// </summary>
#pragma warning disable S2094 // Empty class - required polyfill, cannot add members
    internal static class IsExternalInit { }
#pragma warning restore S2094
}
