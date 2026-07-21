using Colossal.UI.Binding;

namespace CivicSurvival.Core.UI
{
    /// <summary>
    /// Registrar for UI bindings. Allows panels to register bindings
    /// without direct dependency on UISystemBase.
    ///
    /// CS2's UISystemBase accepts static value/trigger bindings and separately
    /// tracks update bindings such as GetterValueBinding.
    /// </summary>
    public interface IBindingRegistrar
    {
        /// <summary>
        /// Register any CS2 UI binding (RawValueBinding, ValueBinding, GetterValueBinding, TriggerBinding).
        /// </summary>
        void AddBinding(IBinding binding);

        /// <summary>
        /// Register a binding that UISystemBase must update every tick.
        /// </summary>
        void AddUpdateBinding(IUpdateBinding binding);
    }
}
