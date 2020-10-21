# GVFS Unit Tests

## Unit Test Projects

### GVFS.UnitTests.Windows

* Targets .NET Framework
* Contains all unit tests that depend on .NET Framework assemblies
* Has links (in the `NetCore` folder) to all of the unit tests in GVFS.UnitTests 

GVFS.UnitTests.Windows links in all of the tests from GVFS.UnitTests to ensure that they pass on both the .NET Core and .Net Framework platforms.

## Running Unit Tests

**Option 1: `Scripts\RunUnitTests.bat`**

`RunUnitTests.bat` will run GVFS.UnitTests.Windows

**Option 2: Run individual projects from Visual Studio**

GVFS.UnitTests.Windows can both be run from Visual Studio.  Simply set either as the StartUp project and run them from the IDE.

