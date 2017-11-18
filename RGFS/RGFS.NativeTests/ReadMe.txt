========================================================================
RGFS.NativeTests
========================================================================

Summary:

RGFS.NativeTests is a library used by RGFS.FunctionalTests to run RGFS
functional tests using the native WinAPI.

The RGFS.NativeTests dll is output into the appropriate RGFS.FunctionalTests
directory so that the dll can be found when it is DllImported by RGFS.NativeTests.


Folder Structure:

interface -> Header files that are consumable by projects outside of RGFS.NativeTests
include   -> Header files that are internal to RGFS.NativeTests
source    -> RGFS.NativeTests source code


Debugging:

To step through tests in RGFS.NativeTests and to set breakpoints, ensure that the 
"Enable native code debugging" setting is checked in the RGFS.FunctionalTests 
project properites (Debug tab)