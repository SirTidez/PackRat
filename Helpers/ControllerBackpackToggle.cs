#if MONO
using S1GameInput = ScheduleOne.GameInput;
using S1InteractionManagerSingleton = ScheduleOne.DevUtilities.Singleton<ScheduleOne.Interaction.InteractionManager>;
#else
using S1GameInput = Il2CppScheduleOne.GameInput;
using S1InteractionManagerSingleton = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Interaction.InteractionManager>;
#endif

namespace PackRat.Helpers;

/// <summary>
/// Observes the game's controller Interact action so a controller can open a backpack when
/// that action is otherwise unclaimed. PackRat deliberately does not register or own an input
/// action here; the game's input system remains the authority for controller scheme switching.
/// </summary>
public static class ControllerBackpackToggle
{
    /// <summary>
    /// Gets whether the gamepad Interact action was pressed this frame while no interactable is
    /// being hovered or actively interacted with.
    /// </summary>
    public static bool WasPressedThisFrame()
    {
        try
        {
            if (!S1GameInput.GetCurrentInputDeviceIsGamepad() ||
                S1GameInput.IsTyping ||
                !S1GameInput.GetButtonDown(S1GameInput.ButtonCode.Interact))
            {
                return false;
            }

            var interactionManager = S1InteractionManagerSingleton.Instance;
            return interactionManager == null ||
                   (interactionManager.HoveredInteractableObject == null &&
                    interactionManager.InteractedObject == null);
        }
        catch
        {
            // Game input and interaction singletons may not be initialized during loading.
            return false;
        }
    }
}
