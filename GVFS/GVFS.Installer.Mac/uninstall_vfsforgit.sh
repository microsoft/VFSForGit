#!/bin/bash

KEXTFILENAME="PrjFSKext.kext"
VFSFORDIRECTORY="/usr/local/vfsforgit"
PRJFSKEXTDIRECTORY="/Library/Extensions"
LAUNCHDAEMONDIRECTORY="/Library/LaunchDaemons"
LAUNCHAGENTDIRECTORY="/Library/LaunchAgents"
LOGDAEMONLAUNCHDFILENAME="org.vfsforgit.prjfs.PrjFSKextLogDaemon.plist"
SERVICEAGENTLAUNCHDFILENAME="org.vfsforgit.service.plist"
GVFSCOMMANDPATH="/usr/local/bin/gvfs"
UNINSTALLERCOMMANDPATH="/usr/local/bin/uninstall_vfsforgit.sh"
INSTALLERPACKAGEID="com.vfsforgit.pkg"
KEXTID="org.vfsforgit.PrjFSKext"

function UnloadKext()
{
    kextLoaded=`/usr/sbin/kextstat -b "$KEXTID" | wc -l`
    if [ $kextLoaded -eq "2" ]; then
        unloadCmd="sudo /sbin/kextunload -b $KEXTID"
        echo "$unloadCmd..."
        eval $unloadCmd || exit 1
    fi
}

function UnInstallVFSForGit()
{
    if [ -d "${PRJFSKEXTDIRECTORY}/$KEXTFILENAME" ]; then
        rmCmd="sudo /bin/rm -Rf ${PRJFSKEXTDIRECTORY}/$KEXTFILENAME"
        echo "$rmCmd..."
        eval $rmCmd || { echo "Error: Could not delete ${PRJFSKEXTDIRECTORY}/$KEXTFILENAME. Delete it manually."; exit 1; }
    fi
    
    if [ -f "${LAUNCHDAEMONDIRECTORY}/$LOGDAEMONLAUNCHDFILENAME" ]; then
        # Check if the daemon is loaded. Unload only if necessary.
        isLoadedCmd="sudo launchctl kill SIGCONT system/org.vfsforgit.prjfs.PrjFSKextLogDaemon"
        echo "$isLoadedCmd"
        if $isLoadedCmd; then
            unloadCmd="sudo launchctl unload -w ${LAUNCHDAEMONDIRECTORY}/$LOGDAEMONLAUNCHDFILENAME"
            echo "$unloadCmd..."
            eval $unloadCmd || { echo "Error: Could not unload ${LAUNCHDAEMONDIRECTORY}/$LOGDAEMONLAUNCHDFILENAME. Unload it manually (\"$unloadCmd\")."; exit 1; }
        fi
        
        rmCmd="sudo /bin/rm -Rf ${LAUNCHDAEMONDIRECTORY}/$LOGDAEMONLAUNCHDFILENAME"
        echo "$rmCmd..."
        eval $rmCmd || { echo "Error: Could not delete ${LAUNCHDAEMONDIRECTORY}/$LOGDAEMONLAUNCHDFILENAME. Delete it manually."; exit 1; }
    fi
        
    # Unloading Service LaunchAgent for each user
    # There will be one loginwindow instance for each logged in user, 
    # get its uid (this will correspond to the logged in user's id.) 
    # Then use launchctl bootout gui/uid to unload the Service 
    # for each user.
    if [ -f "${LAUNCHAGENTDIRECTORY}/$SERVICEAGENTLAUNCHDFILENAME" ]; then
        for uid in $(ps -Ac -o uid,command | grep -iw "loginwindow" | awk '{print $1}'); do
            # Check if the Service is loaded. Unload only if necessary.
            isLoadedCmd="sudo launchctl kill SIGCONT gui/$uid/org.vfsforgit.service"
            echo "$isLoadedCmd"
            if $isLoadedCmd; then
                unloadCmd="sudo launchctl bootout gui/$uid ${LAUNCHAGENTDIRECTORY}/$SERVICEAGENTLAUNCHDFILENAME"
                echo "$unloadCmd..."
                eval $unloadCmd || { echo "Error: Could not unload ${LAUNCHAGENTDIRECTORY}/$SERVICEAGENTLAUNCHDFILENAME. Unload it manually (\"$unloadCmd\")."; exit 1; }
            fi
        done
        
        rmCmd="sudo /bin/rm -Rf ${LAUNCHAGENTDIRECTORY}/$SERVICEAGENTLAUNCHDFILENAME"
        echo "$rmCmd..."
        eval $rmCmd || { echo "Error: Could not delete ${LAUNCHAGENTDIRECTORY}/$SERVICEAGENTLAUNCHDFILENAME. Delete it manually."; exit 1; }
    fi
    
    if [ -s "${GVFSCOMMANDPATH}" ]; then
        rmCmd="sudo /bin/rm -Rf ${GVFSCOMMANDPATH}"
        echo "$rmCmd..."
        eval $rmCmd || { echo "Error: Could not delete ${GVFSCOMMANDPATH}. Delete it manually."; exit 1; }
    fi
    
    if [ -d "${VFSFORDIRECTORY}" ]; then
        rmCmd="sudo /bin/rm -Rf ${VFSFORDIRECTORY}"
        echo "$rmCmd..."
        eval $rmCmd || { echo "Error: Could not delete ${VFSFORDIRECTORY}. Delete it manually."; exit 1; }
    fi
}

function ForgetPackage()
{
    if [ -f "/usr/sbin/pkgutil" ]; then
        forgetCmd="sudo /usr/sbin/pkgutil --forget $INSTALLERPACKAGEID"
        echo "$forgetCmd..."
        eval $forgetCmd
    fi
}

function Run()
{
    UnloadKext
    UnInstallVFSForGit
    ForgetPackage
    echo "Successfully uninstalled VFSForGit"
}

Run
