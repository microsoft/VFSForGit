# Authoring Tests

## Functional Tests

#### Runnable functional test projects

- `GVFS.FunctionalTests.Windows`

`GVFS.FunctionalTests.Windows`, contains functional tests that require Windows. Additionally, `GVFS.FunctionalTests.Windows` includes all the `GVFS.FunctionalTests` allowing it to run both cross-platform and Windows-specific functional tests.

#### Other functional test projects

*GVFS.NativeTests*

`GVFS.NativeTests` contains tests written in C++ that use the Windows API directly.  These tests are called from the managed tests (see above) using PInvoke.

*GVFS.FunctionalTests.LockHolder*

The `LockHolder` is a small program that allows the functional tests to request and release the `GVFSLock`.  `LockHolder` is useful for simulating different timing/race conditions.

## Running the Functional Tests

The functional tests are built on NUnit 3, which is available as a set of NuGet packages.

### Windows

1. Build VFS for Git:
    
    **Option 1:** Open GVFS.sln in Visual Studio and build everything.
    
    **Option 2:** Run `Scripts\BuildGVFSForWindows.bat` from the command line

2. Run the VFS4G installer that was built in step 2.  This will ensure that ProjFS is properly installed/enabled on your machine, and that VFS4G will be able to find the correct version of the pre/post-command hooks. The installer will be placed in `BuildOutput\GVFS.Installer.Windows\bin\x64\<Debug or Release>`
3. Run the tests **with elevation**.  Elevation is required because the functional tests create and delete a test service.

   **Option 1:** Run the `GVFS.FunctionalTests.Windows` project from inside Visual Studio launched as Administrator.
   
   **Option 2:** Run `Scripts\RunFunctionalTests.bat` from CMD launched as Administrator.

#### Selecting Which Tests are Run

By default, the functional tests run a subset of tests as a quick smoke test for developers.  There are three mutually exclusive arguments that can be passed to the functional tests to change this behavior:

- `--full-suite`: Run all configurations of all functional tests
- `--extra-only`: Run only those tests marked as "ExtraCoverage" (i.e. the tests that are not run by default)
- `--windows-only`: Run only the tests marked as being Windows specific

**NOTE** `Scripts\RunFunctionalTests.bat` already uses some of these arguments.  If you run the tests using `RunFunctionalTests.bat` consider locally modifying the script rather than passing these flags as arguments to the script.

### Customizing the Functional Test Settings

The functional tests take a set of parameters that indicate what paths and URLs to work with.  If you want to customize those settings, they
can be found in [`GVFS.FunctionalTests.Windows\Settings.cs`](/GVFS/GVFS.FunctionalTests.Windows/Settings.cs).


## Running Specific Tests

Specific tests can be run by adding the `--test=<comma separated list of tests>` command line argument to the functional test project/scripts.  

Note that the test name must include the class and namespace and that `Debug` or `Release` must be specified when running the functional test scripts.

*Example*

Windows (Script):

`Scripts\RunFunctionalTests.bat Debug --test=GVFS.FunctionalTests.Tests.EnlistmentPerFixture.GitFilesTests.CreateFileTest`

Windows (Visual Studio):

1. Set `GVFS.FunctionalTests.Windows` as StartUp project
2. Project Properties->Debug->Start options->Command line arguments (all on a single line): `--test=GVFS.FunctionalTests.Tests.EnlistmentPerFixture.GitFilesTests.CreateFileTest`

Mac:

`Scripts/Mac/RunFunctionalTests.sh Debug --test=GVFS.FunctionalTests.Tests.EnlistmentPerFixture.GitFilesTests.CreateFileTest`

## How to Write a Functional Test

Each piece of functionality that we add to VFS for Git should have corresponding functional tests that clone a repo, mount the filesystem, and use existing tools and filesystem
APIs to interact with the virtual repo.

Since these are functional tests that can potentially modify the state of files on disk, you need to be careful to make sure each test can run in a clean 
environment.  There are three base classes that you can derive from when writing your tests.  It's also important to put your new class into the same namespace
as the base class, because NUnit treats namespaces like test suites, and we have logic that keys off that for deciding when to create enlistments.

1. `TestsWithLongRunningEnlistment`

    Before any test in this namespace is executed, we create a single enlistment and mount VFS for Git.  We then run all tests in this namespace that derive
	from this base class.  Only put tests in here that are purely readonly and will leave the repo in a good state for future tests.

2. `TestsWithEnlistmentPerFixture`

    For any test fixture (a fixture is the same as a class in NUnit) that derives from this class, we create an enlistment and mount VFS for Git before running
	any of the tests in the fixture, and then we unmount and delete the enlistment after all tests are done (but before any other fixture runs).  If you need
	to write a sequence of tests that manipulate the same repo, this is the right base class.

3. `TestsWithEnlistmentPerTestCase`

   Derive from this class if you need a new enlistment created for each test case.  This is the most reliable, but also most expensive option.

## Updating the Remote Test Branch

By default, the functional tests clone `master`, check out the branch "FunctionalTests/YYYYMMDD" (with the day the FunctionalTests branch was created), 
and then remove all remote tracking information. This is done to guarantee that remote changes to tip cannot break functional tests. If you need to update 
the functional tests to use a new FunctionalTests branch, you'll need to create a new "FunctionalTests/YYYYMMDD" branch and update the `Commitish` setting in `Settings.cs` to have this new branch name.  
Once you have verified your scenarios locally you can push the new FunctionalTests branch and then your changes.
