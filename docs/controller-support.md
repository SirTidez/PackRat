# Controller Support

PackRat controller support uses Schedule I's own controller input and UI-selection systems. No PackRat-specific gamepad binding is required.

## Quick reference

| Action | Controller behavior |
|--------|---------------------|
| Open / close backpack | Press **Interact** — the west face button (`X` on Xbox-style controllers, `Square` on PlayStation-style controllers). |
| Apply a backpack tier | Select the tier item in the hotbar, then press **Interact**. |
| Move selection | Use Schedule I's normal controller UI navigation. PackRat controls join the same selection path as backpack slots and the hotbar. |
| Activate a selected control | Use the game's normal UI submit action. |
| Search | Select Search, then submit it to focus the field and request Steam's gamepad keyboard. |

## Opening behavior

The controller opener is intentionally contextual. PackRat listens to Schedule I's **Interact** action only when all of the following are true:

- A gamepad is the active game input device.
- The player is not typing.
- No world object is hovered or being interacted with.

This means a door, station, NPC, or any other current interactable keeps priority over the backpack. If Interact does not open the backpack, aim away from the current interactable and press it again. The same button can apply a selected PackRat tier item or open/close an already unlocked backpack.

## Browsing the backpack

Once a PackRat surface is open, controller selection covers the item grid and hotbar along with PackRat-owned controls:

- Sort tabs and filter controls
- Search
- Previous/next page controls
- Product metrics open/close control
- Settings and its controls
- Storage, station, and deal-handover PackRat controls when those surfaces are present

The navigation graph is deliberate rather than purely spatial. Moving down from a sort tab returns to the nearest backpack item slot; moving up from a sort tab selects Search. Moving down from Search returns to the nearest sort tab. These paths avoid the selector becoming trapped on a control or jumping to the vanilla hotbar.

Use the game’s normal UI Back action to close the direct backpack browser. Storage and handover surfaces retain their host menu’s normal close behavior.

## Search and the on-screen keyboard

Selecting and submitting Search always focuses PackRat's real search field. PackRat then asks Schedule I to open Steam's gamepad keyboard.

Steam only provides that keyboard when all of these conditions are met:

- A controller is the active game input device.
- The Steam overlay is enabled.
- The game is running on a Steam Deck or in Steam Big Picture mode.

On a regular desktop Steam session, Search still focuses correctly but no Steam keyboard is displayed. PackRat logs a one-time warning explaining that the keyboard is unavailable; that warning is expected. Use a connected physical keyboard to type into the focused search field, or use Steam Deck/Big Picture for gamepad text entry.

## How it is implemented

For maintainers, [`ControllerBackpackToggle`](../Helpers/ControllerBackpackToggle.cs) observes the game’s existing `ButtonCode.Interact` action and checks Schedule I’s interaction manager before `PlayerBackpack` handles a toggle. [`ControllerUiSupport`](../Helpers/ControllerUiSupport.cs) registers PackRat controls with the active native `UIPanel`, initializes runtime `NavigationOverride` data, and removes only its own registrations when a UI surface closes.

Search uses an invisible controller-only proxy because a runtime-added Unity `InputField` does not reliably remain a native selectable. The proxy keeps controller navigation stable, maps its focus outline to the visible Search field, and invokes the game’s native submit trigger so the Steam keyboard request matches Schedule I UI behavior.

For the broader runtime relationship, see [Controller Input and UI Navigation](../ARCHITECTURE.md#controller-input-and-ui-navigation).
