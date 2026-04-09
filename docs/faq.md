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

### Why only Windows? Wasn't there a macOS version coming?

We were working hard to deliver a macOS version, and there are still many
remnants of that effort in the codebase. We were heavily dependent upon the
macOS KAUTH kernel extensions to provide equivalent functionality of ProjFS on
Windows. Unfortunately, [Apple deprecated this feature in Catalina](https://developer.apple.com/support/kernel-extensions/).
All of the recommended alternatives were either not appropriate for our
scenario or were not fast enough.

We transitioned our large repository strategy to focus on using
[`git sparse-checkout`](https://github.blog/2020-01-17-bring-your-monorepo-down-to-size-with-sparse-checkout/)
instead of filesystem virtualization. We then forked the VFS for Git
codebase to create [Scalar](https://github.com/microsoft/scalar). Those
investments in a cross-platform tool paid off since Scalar could launch
quickly.

### Why are you abandoning VFS for Git?

We will continue supporting VFS for Git as long as there is a need for it.
Through our experience, we have found that it is appropriate for only a very
small number of extremely large repos. For instance, the Windows OS repository
will depend on VFS for Git for the foreseeable future, and we will continue
supporting them. This includes updating VFS for Git with new versions of Git.

The basic issue with VFS for Git is that users who don’t know exactly what’s
going on will frequently get into bad states, such as populating too much of
their working directory or not properly enabling the ProjFS feature (this is
a larger problem for users using older versions of Windows). The Windows OS
team developed a lot of tribal knowledge for how to avoid known issues with
VFS for Git.

We prefer users adopting Scalar because that is where we are investing most
of our engineering efforts. The system is simpler and has a better “offramp”
onto core Git, if one decides that they want to get there. We are also
investing in making the Git client do more of the heavy lifting and reducing
how much is really a “Scalar” feature and how much is just a Git feature.
