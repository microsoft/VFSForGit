========================================================================
GVFS.NativeTests
========================================================================

Summary:

GVFS.NativeTests is a library used by GVFS.FunctionalTests to run GVFS
functional tests using the native WinAPI.

The GVFS.NativeTests dll is output into the appropriate GVFS.FunctionalTests
directory so that the dll can be found when it is DllImported by GVFS.NativeTests.


Folder Structure:

interface -> Header files that are consumable by projects outside of GVFS.NativeTests
include   -> Header files that are internal to GVFS.NativeTests
source    -> GVFS.NativeTests source code


Debugging:

To step through tests in GVFS.NativeTests and to set breakpoints, ensure that the 
"Enable native code debugging" setting is checked in the GVFS.FunctionalTests 
project properites (Debug tab)