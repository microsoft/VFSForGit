using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Hooks.HooksPlatform;
using System;
using System.IO;
using System.Linq;

namespace GVFS.Hooks
{
    public partial class Program
    {
        private static string GetWorktreeSubcommand(string[] args)
        {
            return WorktreeCommandParser.GetSubcommand(args);
        }

        /// <summary>
        /// Gets a positional argument from git worktree subcommand args.
        /// For 'add': git worktree add [options] &lt;path&gt; [&lt;commit-ish&gt;]
        /// For 'remove': git worktree remove [options] &lt;worktree&gt;
        /// For 'move': git worktree move [options] &lt;worktree&gt; &lt;new-path&gt;
        /// </summary>
        private static string GetWorktreePositionalArg(string[] args, int positionalIndex)
        {
            return WorktreeCommandParser.GetPositionalArg(args, positionalIndex);
        }

        private static string GetWorktreePathArg(string[] args)
        {
            return WorktreeCommandParser.GetPathArg(args);
        }

        private static void RunWorktreePreCommand(string[] args)
        {
            string subcommand = GetWorktreeSubcommand(args);
            switch (subcommand)
            {
                case "add":
                    BlockNestedWorktreeAdd(args);
                    break;
                case "remove":
                    HandleWorktreeRemove(args);
                    break;
                case "move":
                    // Unmount at old location before git moves the directory
                    UnmountWorktreeByArg(args);
                    break;
            }
        }

        private static void RunWorktreePostCommand(string[] args)
        {
            string subcommand = GetWorktreeSubcommand(args);
            switch (subcommand)
            {
                case "add":
                    MountNewWorktree(args);
                    break;
                case "remove":
                    RemountWorktreeIfRemoveFailed(args);
                    CleanupSkipCleanCheckMarker(args);
                    break;
                case "move":
                    // Mount at the new location after git moved the directory
                    MountMovedWorktree(args);
                    break;
            }
        }

        private static void UnmountWorktreeByArg(string[] args)
        {
            string worktreePath = GetWorktreePathArg(args);
            if (string.IsNullOrEmpty(worktreePath))
            {
                return;
            }

            string fullPath = ResolvePath(worktreePath);
            if (!UnmountWorktree(fullPath))
            {
                Console.Error.WriteLine(
                    $"error: failed to unmount worktree '{fullPath}'. Cannot proceed with move.");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// If the worktree directory and its .git file both still exist after
        /// git worktree remove, the removal failed completely. Remount ProjFS
        /// so the worktree remains usable. If the remove partially succeeded
        /// (e.g., .git file or gitdir removed), don't attempt recovery.
        /// </summary>
        private static void RemountWorktreeIfRemoveFailed(string[] args)
        {
            string worktreePath = GetWorktreePathArg(args);
            if (string.IsNullOrEmpty(worktreePath))
            {
                return;
            }

            string fullPath = ResolvePath(worktreePath);
            string dotGitFile = Path.Combine(fullPath, ".git");
            if (Directory.Exists(fullPath) && File.Exists(dotGitFile))
            {
                ProcessHelper.Run("gvfs", $"mount \"{fullPath}\"", redirectOutput: false);
            }
        }

        /// <summary>
        /// Remove the skip-clean-check marker if it still exists after
        /// worktree remove completes (e.g., if the remove failed and the
        /// worktree gitdir was not deleted).
        /// </summary>
        private static void CleanupSkipCleanCheckMarker(string[] args)
        {
            string worktreePath = GetWorktreePathArg(args);
            if (string.IsNullOrEmpty(worktreePath))
            {
                return;
            }

            string fullPath = ResolvePath(worktreePath);
            GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(fullPath);
            if (wtInfo != null)
            {
                string markerPath = Path.Combine(wtInfo.WorktreeGitDir, GVFSConstants.DotGit.SkipCleanCheckName);
                if (File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }
            }
        }

        /// <summary>
        /// Block creating a worktree inside the primary VFS working directory
        /// or inside any other existing worktree.
        /// ProjFS cannot handle nested virtualization roots.
        /// </summary>
        private static void BlockNestedWorktreeAdd(string[] args)
        {
            string worktreePath = GetWorktreePathArg(args);
            if (string.IsNullOrEmpty(worktreePath))
            {
                return;
            }

            string fullPath = ResolvePath(worktreePath);
            string primaryWorkingDir = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName);

            if (GVFSEnlistment.IsPathInsideDirectory(fullPath, primaryWorkingDir))
            {
                Console.Error.WriteLine(
                    $"error: cannot create worktree inside the VFS working directory.\n" +
                    $"Create the worktree outside of '{primaryWorkingDir}'.");
                Environment.Exit(1);
            }

            string gitDir = Path.Combine(primaryWorkingDir, ".git");
            foreach (string existingWorktreePath in GVFSEnlistment.GetKnownWorktreePaths(gitDir))
            {
                if (GVFSEnlistment.IsPathInsideDirectory(fullPath, existingWorktreePath))
                {
                    Console.Error.WriteLine(
                        $"error: cannot create worktree inside an existing worktree.\n" +
                        $"'{fullPath}' is inside worktree '{existingWorktreePath}'.");
                    Environment.Exit(1);
                }
            }
        }

        private static void HandleWorktreeRemove(string[] args)
        {
            string worktreePath = GetWorktreePathArg(args);
            if (string.IsNullOrEmpty(worktreePath))
            {
                return;
            }

            string fullPath = ResolvePath(worktreePath);
            GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(fullPath);

            bool hasForce = args.Any(a =>
                a.Equals("--force", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-f", StringComparison.OrdinalIgnoreCase));

            // Check if the worktree's GVFS mount is running by probing the pipe.
            bool isMounted = false;
            if (wtInfo != null)
            {
                string pipeName = GVFSHooksPlatform.GetNamedPipeName(enlistmentRoot) + wtInfo.PipeSuffix;
                using (NamedPipeClient pipeClient = new NamedPipeClient(pipeName))
                {
                    isMounted = pipeClient.Connect(500);
                }
            }

            if (!hasForce)
            {
                if (!isMounted)
                {
                    Console.Error.WriteLine(
                        $"error: worktree '{fullPath}' is not mounted.\n" +
                        $"Mount it with 'gvfs mount \"{fullPath}\"' or use 'git worktree remove --force'.");
                    Environment.Exit(1);
                }

                // Check for uncommitted changes while ProjFS is still mounted.
                ProcessResult statusResult = ProcessHelper.Run(
                    "git",
                    $"-C \"{fullPath}\" status --porcelain",
                    redirectOutput: true);

                if (!string.IsNullOrWhiteSpace(statusResult.Output))
                {
                    Console.Error.WriteLine(
                        $"error: worktree '{fullPath}' has uncommitted changes.\n" +
                        $"Use 'git worktree remove --force' to remove it anyway.");
                    Environment.Exit(1);
                }
            }
            else if (!isMounted)
            {
                // Force remove of unmounted worktree — nothing to unmount.
                return;
            }

            // Write a marker in the worktree gitdir that tells git.exe
            // to skip the cleanliness check during worktree remove.
            // We already did our own check above while ProjFS was alive.
            string skipCleanCheck = Path.Combine(wtInfo.WorktreeGitDir, GVFSConstants.DotGit.SkipCleanCheckName);
            File.WriteAllText(skipCleanCheck, string.Empty);

            // Unmount ProjFS before git deletes the worktree directory.
            if (!UnmountWorktree(fullPath, wtInfo) && !hasForce)
            {
                Console.Error.WriteLine(
                    $"error: failed to unmount worktree '{fullPath}'.\n" +
                    $"Use 'git worktree remove --force' to attempt removal anyway.");
                Environment.Exit(1);
            }
        }

        private static bool UnmountWorktree(string fullPath)
        {
            GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(fullPath);
            if (wtInfo == null)
            {
                return false;
            }

            return UnmountWorktree(fullPath, wtInfo);
        }

        private static bool UnmountWorktree(string fullPath, GVFSEnlistment.WorktreeInfo wtInfo)
        {
            ProcessResult result = ProcessHelper.Run("gvfs", $"unmount \"{fullPath}\"", redirectOutput: false);

            // After gvfs unmount exits, ProjFS handles may still be closing.
            // Wait briefly to allow the OS to release all handles before git
            // attempts to delete the worktree directory.
            System.Threading.Thread.Sleep(200);

            return result.ExitCode == 0;
        }

        private static void MountNewWorktree(string[] args)
        {
            string worktreePath = GetWorktreePathArg(args);
            if (string.IsNullOrEmpty(worktreePath))
            {
                return;
            }

            string fullPath = ResolvePath(worktreePath);

            // Verify worktree was created (check for .git file)
            string dotGitFile = Path.Combine(fullPath, ".git");
            if (File.Exists(dotGitFile))
            {
                string worktreeError;
                GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(fullPath, out worktreeError);
                if (worktreeError != null)
                {
                    Console.Error.WriteLine($"warning: failed to read worktree info for '{fullPath}': {worktreeError}");
                }

                // Store the primary enlistment root so mount/unmount can find
                // it without deriving from path structure assumptions.
                if (wtInfo?.WorktreeGitDir != null)
                {
                    string markerPath = Path.Combine(
                        wtInfo.WorktreeGitDir,
                        GVFSEnlistment.WorktreeInfo.EnlistmentRootFileName);
                    File.WriteAllText(markerPath, enlistmentRoot);
                }

                // Copy the primary's index to the worktree before checkout.
                // The primary index has all entries with correct skip-worktree
                // bits. If the worktree targets the same commit, checkout is
                // a no-op. If a different commit, git does an incremental
                // update — much faster than building 2.5M entries from scratch.
                if (wtInfo?.SharedGitDir != null)
                {
                    string primaryIndex = Path.Combine(wtInfo.SharedGitDir, "index");
                    string worktreeIndex = Path.Combine(wtInfo.WorktreeGitDir, "index");
                    if (File.Exists(primaryIndex) && !File.Exists(worktreeIndex))
                    {
                        // Copy to a temp file first, then rename atomically.
                        // The primary index may be updated concurrently by the
                        // running mount; a direct copy risks a torn read on
                        // large indexes (200MB+ in some large repos).
                        // Note: mirrors PhysicalFileSystem.TryCopyToTempFileAndRename
                        // but that method requires GVFSPlatform which is not
                        // available in the hooks process.
                        string tempIndex = worktreeIndex + ".tmp";
                        try
                        {
                            File.Copy(primaryIndex, tempIndex, overwrite: true);
                            File.Move(tempIndex, worktreeIndex);
                        }
                        catch
                        {
                            try { File.Delete(tempIndex); } catch { }
                            throw;
                        }
                    }
                }

                // Run checkout to reconcile the index with the worktree's HEAD.
                // With a pre-populated index this is fast (incremental diff).
                // Override core.virtualfilesystem with an empty script that
                // returns .gitattributes so it gets materialized while all
                // other entries keep skip-worktree set.
                //
                // Disable hooks via core.hookspath — the worktree's GVFS mount
                // doesn't exist yet, so post-index-change would fail trying
                // to connect to a pipe that hasn't been created.
                string emptyVfsHook = Path.Combine(fullPath, ".vfs-empty-hook");
                try
                {
                    File.WriteAllText(emptyVfsHook, "#!/bin/sh\nprintf \".gitattributes\\n\"\n");
                    string emptyVfsHookGitPath = emptyVfsHook.Replace('\\', '/');

                    ProcessHelper.Run(
                        "git",
                        $"-C \"{fullPath}\" -c core.virtualfilesystem=\"{emptyVfsHookGitPath}\" -c core.hookspath= checkout -f HEAD",
                        redirectOutput: false);
                }
                finally
                {
                    File.Delete(emptyVfsHook);
                }

                // Hydrate .gitattributes — copy from the primary enlistment.
                if (wtInfo?.SharedGitDir != null)
                {
                    string primarySrc = Path.GetDirectoryName(wtInfo.SharedGitDir);
                    string primaryGitattributes = Path.Combine(primarySrc, ".gitattributes");
                    string worktreeGitattributes = Path.Combine(fullPath, ".gitattributes");
                    if (File.Exists(primaryGitattributes) && !File.Exists(worktreeGitattributes))
                    {
                        File.Copy(primaryGitattributes, worktreeGitattributes);
                    }
                }

                // Now mount GVFS — the index exists for GitIndexProjection
                ProcessHelper.Run("gvfs", $"mount \"{fullPath}\"", redirectOutput: false);
            }
        }

        private static void MountMovedWorktree(string[] args)
        {
            // git worktree move <worktree> <new-path>
            // After move, the worktree is at <new-path>
            string newPath = GetWorktreePositionalArg(args, 1);
            if (string.IsNullOrEmpty(newPath))
            {
                return;
            }

            string fullPath = ResolvePath(newPath);

            string dotGitFile = Path.Combine(fullPath, ".git");
            if (File.Exists(dotGitFile))
            {
                ProcessHelper.Run("gvfs", $"mount \"{fullPath}\"", redirectOutput: false);
            }
        }
    }
}
