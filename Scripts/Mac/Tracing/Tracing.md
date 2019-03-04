# Tracing on macOS

[DTrace](https://en.wikipedia.org/wiki/DTrace) is the main low-level tool for tracing and diagnostics on macOS.
For diagnosing issues potentially caused by VFS for Git, tracing syscalls to track down errors has proven to be a useful technique on Windows.
On macOS, the DTrace script `dtruss` which ships with the OS can be used for tracing system calls made by processes.
However, it has not been updated for newer OS versions for a while, making some of its features unreliable.
We therefore recommend using our [updated version of the script](./dtruss), and the instructions below assume you are using this version.

## TL;DR: This build command is failing/misbehaving and I've been asked to collect a trace

To capture a trace of the syscalls made by the failing command, run the following:

    ++path-to-vfs4g-source++/Scripts/Mac/Tracing/dtruss -d -e -F -f    ++command-to-run++   2> >(tee   ++trace-filename.txt++   >&2)

## General tips

`dtruss` simply outputs a live list of all syscalls (or all calls to one specific syscall) made by the selected/matching process(es), including return value/errno and arguments.
Arguments are formatted for human readability to some extent. (e.g. string arguments are typically printed as a string, not just the raw `char` pointer.)

The output is written to stderr, so to save it to a file use `2> filename.txt`, or to both save it to file *and* print it to the terminal, this will work in `bash`:

    sudo dtruss -p 1000    2> >(tee clang-trace.txt >&2)

When starting `dtruss`, wait for the column headers to appear before starting whatever activity you're trying to trace; dtrace can take a few seconds to compile and inject the script, and events that occur before this is done will be dropped.
This is of course not an issue if you are starting the traced command on the `dtruss` command line directly.
The column headers will look something like this, the exact headers will depend on the command line flags passed:

    	PID/THRD  RELATIVE  ELAPSD SYSCALL(args) 		 = return

## Specifying processes to trace

### Named processes

Use `-n <processname>`, for example if there seems to be a problem with syscalls made by `clang`:

    sudo ./Scripts/Mac/Tracing/dtruss -d -e -n clang

Note that process names in macOS are limited to `MAXCOMLEN`, i.e. 16 characters.

The `-d` and `-e` flags enable printing of time stamps (in µs, where 0 = time when `dtruss` was started) and elapsed wall time during the syscall (also µs), respectively. 

### By PID

Use `-p <PID>` in place of `-n`.

### Tracing a command

For tracing processes started from the command line, launching the command directly together with dtruss is typically the most convenient. To trace the `ls` command, simply use:

    ./Scripts/Mac/Tracing/dtruss ls 

Note the lack of `sudo` on this command. This was required on stock `dtruss` but meant all commands launched this way ran as the root user. Instead, the script now runs only `dtrace` itself as root via sudo, so you will still need to be an admin (`wheel` group) and type your password.

### Tracing child processes

Often, the thing you're trying to diagnose is a more complex mix of multiple processes; for example, building some software from source will typically kick off many different processes for the build system itself, compilers, linkers, and other tools. For tracing the activity of the whole build, it's often most convenient to trace the root build command *and all processes launched by it, recursively.*
`dtruss` supports this via the `-f` flag, which can be combined with any of the other process selectors. For example:

     ./Scripts/Mac/Tracing/dtruss -d -e -f xcodebuild

This will run an `xcodebuild` in the current directory, trace that process and any other processes below it in the hierarchy and additionally output time stamps and syscall runtimes.

## Changes over stock `dtruss`

For reference, the following improvements have been made so far:

 * When launching a command with `dtruss`, it's now possible to run the command as a non-root user. Simply run `dtruss [-options] <command>` as a non-privileged admin user; the command will run as this user, and `dtrace` itself will be run as root via `sudo` (you will likely be prompted for your password).
 * Correct logging of `execve()` and `posix_spawn()` syscalls.
 * Improvements to output for syscalls with buffer or string parameters. (Better handling of `NULL` pointers, large buffers, etc.)  
 * A new `-F` option for filtering out common but typically uninteresting syscalls. At the moment, this only filters out `close()` calls returning `EBADF`. Some programs seem to call `close()` on thousands of sequential integers, most of which are not valid file descriptors.
 * The mode for following child processes (`-f`) is much more reliable. Previously, processes created with `posix_spawn()` were typically ignored. (They were typically only detected if the parent process had previously already used `fork()`; a mix of both techniques in the same program is rare, however.) `posix_spawn()` is a very common method for creating new processes on macOS nowadays, especially as `fork()`/`execve()` is explicitly disallowed for Objective-C based apps. Following `posix_spawn()`ed processes is now directly supported and some bugs that tended to occur when following *any* child process have also been fixed.

### Remaining bugs/limitations in `dtruss`

 * `dtruss` no longer exits when the specified command itself exits. (This regression is a consequence of the non-privileged command launch change.)
 * There are still some syscalls which are badly formatted in the trace output.
 * The buffers can fill up and miss events when a lot of traced events occur. This is indicated by the message `dtrace: 6228 dynamic variable drops with non-empty dirty list` - try increasing the dynamic variable memory (e.g. `-b 100m` for 100MB - default is currently 30MB) in this case. Better still, try to narrow the focus of your tracing.
 * Currently, you can trace either all syscalls or just one, not some subset.
