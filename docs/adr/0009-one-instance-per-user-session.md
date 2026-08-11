# One RestCue instance per Windows user session

**Status: accepted**

RestCue permits one **主要執行個體** per interactive Windows user session. Startup claims a session-local named mutex atomically; a **重複執行個體** must not initialize normal services, because RestCue has per-user tray state and local settings. A machine-wide mutex was rejected so a different Windows user session is not blocked from running its own RestCue.

The duplicate path remains a one-button, non-modal warning so the product's no-focus-stealing and no-modal-interruption contract is preserved; confirming it ends the duplicate process without forwarding commands to the primary instance.
