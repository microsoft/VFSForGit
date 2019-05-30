# VFS For Git: Analytics Tooling Design Document

This document details the design of and plan for the **VFS4G analytics tool**. The tool aims to help users identify reasons why their enlistment may be slowing down and the `git` commands running slowly on a VFS4G mounted repository. By collecting and reporting statistics, the power to uncover and hopefully fix problems as they arrive moves to the user, empowering them to use the software to its fullest potential.

## Problem

Over the lifetime of an enlistment, sometimes a user's working directory fills up with too many entries. Generally this means having too many placeholders on their disk (have been read and pulled from remote) or too many modified files. This causes unacceptable losses in performance and greatly hinders users as they go about their daily development cycle. Additionally, if a local enlistment has been in use for a long time, the number of loose object may eventually reach the order of several thousand in which case that also contributes to the loss in performance.

## Provided Statistics

- **Modified paths** - The number of files which have been edited and have been handed over to git. This list may contain just directories but not a reference to all of their children who will accordingly also all have modified paths. The command `git ls-tree -r HEAD` can be used to access the file system without risking unintentionally hydrating files
- **Git index** - The differences between modified paths and the index tracked by git. This might be useful in determining what differences the local repository has to predict the complexity and potential slowdowns of an upcoming commit.
- **Placeholders** - A total number of the placeholders being tracked in VFS4G and a list of the placeholders in the system. The list VFS4G maintains should always track every placeholder object on disk
- **Troublesome programs** - Similar to what is already being logged in the heartbeat, look at which services or programs are responsible for hydrating large amounts of files in the enlistment
- **Command run-times** - A history of the time it takes for git commands to run. This can include averages, trends, and eventually graphs (implemented by the client in the visualization component)

## Background Information

- **Modified paths** is the list of files which have been modified and thus correctly reflect everything on the disk. The paths here point to directories and files which have either been modified, created, or deleted. These files are handed to git when a git command reads its index in order to populate its list of files being tracked
- **Placeholders** are files which have been opened by the local system with a read tag but without a write tag. They are objects tracked by VFS4G since git should not have to internally scan them since it is guaranteed that they have not been modified and accordingly do not have to be rehashed for differences. Their contents are on disk, but control over them has not been entirely ceded over to git. They decrease performance because VFS4G has to track all of them individually, and this can cause `git` commands that need to update the projection to run slowly
- The **Git index** is git's internal list of files which it is tracking for creating  the next `commit`. It is useful to this project by means of comparing it to the list of **placeholders** to deduce if those files should truly be hydrated or if it may have occurred incidentally.
- The **file system** must be carefully considered in the case of working with a mounted repository. VFS4G intercepts file I/O calls at the kernel level to inject its own behavior (the projected file system and if needed a network call to pull down the file which the system asked for in the correct format). Because of this, enumerating directories can cause additional data to be pulled down from the remote by hydrating extra files or further hydrating those that already exist on disk. Accordingly, care must be taken when interacting with the file system lest the tool designed to help users increase enlistment heath end up causing more problems.

## Components

- **API** - The server component of the client-server relationship. Responsible for the logic and computation of statistical data, as well as the assembly and sending of a **json statistics object** over the named pipe to the client. It sits within the `InProgressMount` process and interacts with the VFS4G internals
- **Verb** - The `gvfs statistics` verb will be the component that handles the client side interaction with the API. When run, it will collect and store the statistics from the enlistment to either be displayed immediately or collected for trend purposes to then provide understanding in the future. This is also the point where data can be sent to telemetry for our internal analysis. The verb will also output the data to the console with a tool to help visualize and understand the data. It will output the analytics data to the command line and will also be used during the design period to test the API and verb
- **Utility tools for dehydration** - *Stretch goal* | After the analytics are collected, these components will provide utilities to help the user get their enlistment back to a healthier state. The ideal goal is to have VFS4G suggest uses to the user (EG: `This directory was hydrated by Sublime text editor. You can dehydrate it by running "gvfs dehydrate --confirm -d /path/to/repo"`)
- **Additional visualization tools** - *Stretch goal* | With a comprehensive API in place, the possibility opens up for different visualization tools to be created that consume the json data and display it to the user in different ways, potentially even eventually interfacing with the utility tools for an interactive GUI

## Example of CLI Tool Usage

```terminal
$ gvfs statistics

Repository statistics
Total paths tracked by git:        3,123,456 |  100%
Total number of placeholders:        123,456 |    4%
Total number of modififed paths:       1,234 |   <1%

Total hydration percentage:                       4%
Most hydrated directories:
  98% | src/main    | Primarily hydrated by "Sublime Text Editor"
  79% | src/util    | Primarily hydrated by "Visual Studio 2019"
  58% | assets/img  | Primarily hydrated by "cmd.exe"

Repository status:  Healthy
Recommended action: None

To view git command performance run with "--perf"

$ gvfs statistics --perf

Average command run time
"git add":        40ms
"git commit":    180ms
"git checkout": 1534ms
"git stash":     690ms
"git push":     1203ms
"git merge":    1660ms
"git status":    364ms
"git log":       187ms

$ (user interacts with the enlistment)

$ gvfs statistics

Repository statistics
Total paths tracked by git:        3,123,456 |  100%
Total number of placeholders:      3,523,456 |  111%
Total number of modififed paths:     500,000 |   24%

Total hydration percentage:                     135%
Most hydrated directories:
  142% | src/main    | Primarily hydrated by "Sublime Text Editor"
  139% | src/util    | Primarily hydrated by "Visual Studio 2019"
  135% | assets/img  | Primarily hydrated by "Photoshop"

Repository status:  Over-hydrated
Recommended action: run "gvfs dehydrate --confirm --no-status"
Take action? (y/n): y

Starting dehydration. All of your existing files will be backed up in C:\_git\os\dehydrate_backup\20190530_151645
WARNING: If you abort the dehydrate after this point, the repo may become corrupt

Unmounting...Succeeded
Authenticating...Succeeded
Backing up your files...Succeeded
Downloading git objects...Succeeded
Recreating git index...Succeeded
Mounting...Succeeded

Total paths tracked by git:        3,123,456 |  100%
Total number of placeholders:              0 |    0%
Total number of modififed paths:           0 |    0%
```

## Development Phases

1) **API Design** - Build out the functions needed to garner statistics within the `InProcessMount` process to be reported over a named pipe. These statistics will be sent over the pipe in the **json format** to allow flexibility for other tools in the future to expand upon. Computation will be completed within this API to prevent from overflowing the pipe traffic in certain cases (IE the list of placeholders is incredibly long)
2) **Verb Creation** - This phase will somewhat mirror phase 1 to be used for to debug the API as it is created. The verb will consume the generated analytics data and contain a CLI tool which displays it to the user. Eventually, this will be built upon for telemetry and and other needed purposes
3) **Visualization** - Creation of a more complex tool which will better aid the user in understanding the data with which they are being presented. There are several possibilities for how this may end up working, including drawing a cone or perhaps then entire tree of a directory using color to represent the state of the directory, a heat-map of the directory hydration, or a [WinDirStat](https://windirstat.net/) style graphic
4) **Other statistics** - Here is when the breadth of statistics being reported and analyzed can be expanded to include other useful metrics that may be more complex to analyze or procure
5) **Actions and mitigations** - Build out users' toolkit and integrate it into the system in order to aid users in dehydration processes and guide them to a healthier repository state

## Structural Diagram

![Project Structure](AnalyticsToolDiagram.png)
