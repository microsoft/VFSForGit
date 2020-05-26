Frequently Asked Questions
==========================

Here are some questions that users often have with VFS for Git, but are
unrelated to [troubleshooting issues](troubleshooting.md).

### Why does `gvfs clone` create a `<repo>/src` folder?

VFS for Git integrates with ProjFS to keep track of changes under this `src` folder.
Any activity in this folder is assumed to be important to Git operations. By
creating the `src` folder, we are making it easy for your build system to
create output folders outside the `src` directory. We commonly see systems
create folders for build outputs and package downloads. VFS for Git creates
these folders during its builds.

Your build system may create build artifacts such as `.obj` or `.lib` files
next to your source code. These are commonly "hidden" from Git using
`.gitignore` files. Having such artifacts in your source tree creates
additional work for Git because it needs to look at these files and match them
against the `.gitignore` patterns.

By following the pattern VFS for Git tries to establish and placing your build
intermediates and outputs parallel with the `src` folder and not inside it,
you can help optimize Git command performance for developers in the repository
by limiting the number of files Git needs to consider for many common
operations.

### Why the name change?

This project was formerly known as GVFS (Git Virtual File System). It is
undergoing a rename to VFS for Git. While the rename is in progress, the
code, protocol, built executables, and releases may still refer to the old
GVFS name. See https://github.com/Microsoft/VFSForGit/projects/4 for the
latest status of the rename effort.
