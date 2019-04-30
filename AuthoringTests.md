# Authoring Tests

## Functional Tests

### 1. Running the functional tests

Our functional tests are in the `GVFS.FunctionalTests` project.  They are built on NUnit 3, which is available as a set of NuGet packages.

To run the functional tests:
1. Open GVFS.sln in Visual Studio
2. Build all, which will download the NUnit framework and runner
3. You have three options for how to run the tests, all of which are equivalent.
   1. Run the `GVFS.FunctionalTests` project.  Even better, set it as the default project and hit F5.
   2. Use the command line runner.  After building, execute `Scripts\RunFunctionalTests.bat`
   3. If you want to use Visual Studio's Test Explorer, you need to install the NUnit 3 Test Adapter in VS | Tools | Extensions and Updates.

Running the `GVFS.FunctionalTests` project is probably the most convenient for developers.  `RunFunctionalTests.bat` will be used on the build machines.

The functional tests take a set of parameters that indicate what paths and URLs to work with.  If you want to customize those settings, they
can be found in [`GVFS.FunctionalTests\Settings.cs`](/GVFS/GVFS.FunctionalTests/Settings.cs).

### 2. Running Full Suite of Tests vs. Smoke Tests

By default, the VFS for Git functional tests run a subset of tests as a quick smoke test for developers. To run all tests, pass in the `--full-suite` flag.

### 3. Running specific tests

Specific tests can be run by specifying `--test=<comma separated list of tests>` as the command line arguments to the functional test project.

### 4. How to write a functional test

Each piece of functionality that we add to VFS for Git should have corresponding functional tests that clone a repo, mount the filesystem, and use existing tools and filesystem
APIs to interact with the virtual repo.

Since these are functional tests that can potentially modify the state of files on disk, you need to be careful to make sure each test can run in a clean 
environment.  There are three base classes that you can derive from when writing your tests.  It's also important to put your new class into the same namespace
as the base class, because NUnit treats namespaces like test suites, and we have logic that keys off of that for deciding when to create enlistments.

1. `TestsWithLongRunningEnlistment`

    Before any test in this namespace is executed, we create a single enlistment and mount VFS for Git.  We then run all tests in this namespace that derive
	from this base class.  Only put tests in here that are purely readonly and will leave the repo in a good state for future tests.

2. `TestsWithEnlistmentPerFixture`

    For any test fixture (a fixture is the same as a class in NUnit) that derives from this class, we create an enlistment and mount VFS for Git before running
	any of the tests in the fixture, and then we unmount and delete the enlistment after all tests are done (but before any other fixture runs).  If you need
	to write a sequence of tests that manipulate the same repo, this is the right base class.

3. `TestsWithEnlistmentPerTestCase`

   Derive from this class if you need a brand new enlistment per test case.  This is the most reliable, but also most expensive option.

### 5. Updating the remote test branch

By default, `GVFS.FunctionalTests` clones `master`, checks out the branch "FunctionalTests/YYYYMMDD" (with the day the FunctionalTests branch was created), 
and then removes all remote tracking information. This is done to guarantee that remote changes to tip cannot break functional tests. If you need to update 
the functional tests to use a new FunctionalTests branch, you'll need to create a new "FunctionalTests/YYYYMMDD" branch and update the `Commitish` setting in `Settings.cs` to have this new branch name.  
Once you have verified your scenarios locally you can push the new FunctionalTests branch and then your changes.
