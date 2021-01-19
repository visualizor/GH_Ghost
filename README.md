# GH_Ghost
Ghost - a single component that runs other components

Ghost uses `Rhino.NodeInCode` library to evaluate another Grasshopper component internally on a separate thread so Rhino/Grasshopper GUI does not lock up. Link any output of a built-in Grasshopper component to the "C" parameter of Ghost first. Zoom in to add input or output parameters manually to emulate the target component. Or right click on Ghost and select "Fill Params" to auto-populate. Uncheck the "Unblock queueing" option in the context menu to stop automatic recompute when input parameter changes are detected. Otherwise a new solution will immediately start with current input values once old solution completes. 

There are likely some unresolved stability issues. Sometimes re-enabling or recomputing Ghost can take care of those. Always back up files. Ghost works even if a disabled target component is linked up so keep a disabled copy of target component in case Ghost misbehaves. A second GhostBuster component can display how many background workers are currently computing. Ghost components that are deleted prematurely may leave behind straggler processes that can only be killed by a Rhino shut-down. 

Ghost does not touch Grasshopper's preview meshing so UI will still hang on complex surface previewing
