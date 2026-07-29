# PackRat Runtime Layout Contract

## Surface Hierarchy

Use a full-stretch overlay only to block input and dim the world. Put a centered fixed-size card
inside it. Put header, tabs, and content inside the card. Never make the card itself full stretch.

## Dynamic Content

Use a vertical layout group for settings rows. Each row owns label, value, and optional actions
through a horizontal layout group and layout elements. Refresh rows only after configuration
changes; do not rebuild them every frame.

Use the fixed backpack card and its grid geometry for inventory. Search, filters, and sort produce
a transient slot projection; they do not mutate storage order or shrink the card.

## Assets and Interaction

Use a real UI PNG sprite for custom icons, preserve its aspect ratio, and make it non-raycastable.
Use a nine-sliced sprite for scalable rounded backgrounds. The Button owns click handling; its
stretched label and icon are presentation-only children.
