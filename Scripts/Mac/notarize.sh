#!/bin/bash

# This file was sourced from https://github.com/microsoft/BuildXL/blob/8c2348ff04e6ca78726bb945fb2a0f6a55a5c7d6/Private/macOS/notarize.sh
#
# For detailed explanation see: https://developer.apple.com/documentation/security/notarizing_your_app_before_distribution/customizing_the_notarization_workflow

usage() {
    cat <<EOM
$(basename $0) - Handy script to notarize a kernel extension file (KEXT)
Usage: $(basename $0) -id <apple_id> -p <password> -k <path_to_kext>
        -id or --appleid         # A valid Apple ID email address, account must have correct certificates available
        -p  or --password        # The password for the specified Apple ID or Apple One-Time password (to avoid 2FA)
        -k  or --kext            # The path to an already signed kernel extension .kext file
EOM
    exit 0
}

declare arg_AppleId=""
declare arg_Password=""
declare arg_KextPath=""

[ $# -eq 0 ] && { usage; }

function parseArgs() {
    arg_Positional=()
    while [[ $# -gt 0 ]]; do
        cmd="$1"
        case $cmd in
        --help | -h)
            usage
            shift
            exit 0
            ;;
        --appleid | -id)
            arg_AppleId=$2
            shift
            ;;
        --password | -p)
            arg_Password="$2"
            shift
            ;;
        --kext | -k)
            arg_KextPath="$2"
            shift
            ;;
        *)
            arg_Positional+=("$1")
            shift
            ;;
        esac
    done
}

parseArgs "$@"

if [[ -z $arg_AppleId ]]; then
    echo "[ERROR] Must supply valid / non-empty Apple ID!"
    exit 1
fi

if [[ -z $arg_Password ]]; then
    echo "[ERROR] Must supply valid / non-empty password!"
    exit 1
fi

if [[ ! -d "$arg_KextPath" ]]; then
    echo "[ERROR] Must supply valid / non-empty path to KEXT to notarize!"
    exit 1
fi

declare bundle_id=`/usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" ${arg_KextPath}/Contents/Info.plist`

if [[ -z $bundle_id ]]; then
    echo "[ERROR] No CFBundleIdentifier found in KEXT Info.plist!"
    exit 1
fi

echo "Notarizating $arg_KextPath"
declare kext_zip="${arg_KextPath}.zip"

if [[ -f "$kext_zip" ]]; then
    rm -f "$kext_zip"
fi

echo -e "Current state:\n"
xcrun stapler validate -v "$arg_KextPath"

if [[ $? -eq 0 ]]; then
    echo "$arg_KextPath already notarized and stapled, nothing to do!"
    exit 0
fi

set -e

echo "Creating zip file..."
ditto -c -k --rsrc --keepParent "$arg_KextPath" "$kext_zip"

declare start_time=$(date +%s)

declare output="/tmp/progress.xml"

echo "Uploading zip to notarization service, please wait..."
xcrun altool --notarize-app -t osx -f $kext_zip --primary-bundle-id $bundle_id -u $arg_AppleId -p $arg_Password --output-format xml | tee $output

declare request_id=$(/usr/libexec/PlistBuddy -c "print :notarization-upload:RequestUUID" $output)

echo "Checking notarization request validity..."
if [[ $request_id =~ ^\{?[A-F0-9a-f]{8}-[A-F0-9a-f]{4}-[A-F0-9a-f]{4}-[A-F0-9a-f]{4}-[A-F0-9a-f]{12}\}?$ ]]; then
    declare attempts=5

    while :
    do
        echo "Waiting a bit before checking on notarization status again..."

        sleep 20
        xcrun altool --notarization-info $request_id -u $arg_AppleId -p $arg_Password --output-format xml | tee $output

        declare status=$(/usr/libexec/PlistBuddy -c "print :notarization-info:Status" $output)
        echo "Status: $status"

        if [[ -z $status ]]; then
            echo "Left attempts: $attempts"

            if (($attempts <= 0)); then
                break
            fi

            ((attempts--))
        else
            if [[ $status != "in progress" ]]; then
                break
            fi
        fi
    done

    declare end_time=$(date +%s)
    echo -e "Completed in $(($end_time-$start_time)) seconds\n"

    if [[ "$status" != "success" ]]; then
        echo "Error notarizing, exiting..." >&2
        exit 1
    else
        declare url=$(/usr/libexec/PlistBuddy -c "print :notarization-info:LogFileURL" $output)

        if [ "$url" ]; then
            curl $url
        fi

        # Staple the ticket to the kext
        xcrun stapler staple "$arg_KextPath"

        echo -e "State after notarization:\n"
        xcrun stapler validate -v "$arg_KextPath"
        echo -e "Stapler exit code: $? (must be zero on success!)\n"
    fi
else
    echo "Invalid request id found in 'altool' output, aborting!" >&2
    exit 1
fi