## Successful Mounts
```
let start = ago(10d); 
MicrosoftGit
| where timestamp > start
| where name == "Microsoft.Git.GVFS.GVFSMount"
| extend d = parse_json(tostring(parse_json(data).Json))
| extend MountId = tostring(parse_json(data).MountId)
| extend Version = tostring(d.Version) 
| where Version == "0.2.173.2"
| where MountId <> ""
| join kind=leftanti (
    MicrosoftGit
    | where timestamp > start
    | where name == "Microsoft.Git.GVFS.Error"
    | extend d = parse_json(tostring(parse_json(data).Json))
    | extend MountId = tostring(parse_json(data).MountId)
    | extend Exception = tostring(d.Exception)
    | extend ErrorMessage = tostring(d.ErrorMessage)
    | summarize count() by MountId) on MountId  
```
## Unsuccessful Mounts
```
let start = ago(10d); 
MicrosoftGit
| where timestamp > start
| where name == "Microsoft.Git.GVFS.GVFSMount"
| extend d = parse_json(tostring(parse_json(data).Json))
| extend MountId = tostring(parse_json(data).MountId)
| extend Version = tostring(d.Version) 
| where Version == "0.2.173.2"
| where MountId <> ""
| join (
    MicrosoftGit
    | where timestamp > start
    | where name == "Microsoft.Git.GVFS.Error"
    | extend d = parse_json(tostring(parse_json(data).Json))
    | extend MountId = tostring(parse_json(data).MountId)
    | extend Exception = tostring(d.Exception)
    | extend ErrorMessage = tostring(d.ErrorMessage)
    | summarize count() by MountId) on MountId  
```
## Failures on startup
```
let start = ago(10d); 
MicrosoftGit
| where timestamp > start
| where name == "Microsoft.Git.GVFS.GVFSMount"
| extend d = parse_json(tostring(parse_json(data).Json))
| extend MountId = tostring(parse_json(data).MountId)
| extend Version = tostring(d.Version) 
| where Version == "0.2.173.2"
| where MountId <> ""
| join (
    MicrosoftGit
    | where timestamp > start
    | where name == "Microsoft.Git.GVFS.Error"
    | extend d = parse_json(tostring(parse_json(data).Json))
    | extend MountId = tostring(parse_json(data).MountId)
    | extend Exception = tostring(d.Exception)
    | extend ErrorMessage = tostring(d.ErrorMessage)
    | summarize count() by MountId) on MountId  
| join kind=leftanti (
   MicrosoftGit
    | where timestamp > start
    | where name == "Microsoft.Git.GVFS.Mount"
    | extend d = parse_json(tostring(parse_json(data).Json))
    | extend MountId = tostring(parse_json(data).MountId)
    | extend Message = tostring(d.Message)
    | where MountId <> ""
    | where Message == "Virtual repo is ready") on MountId
```
## Failures after successful startup
```
let start = ago(10d); 
MicrosoftGit
| where timestamp > start
| where name == "Microsoft.Git.GVFS.GVFSMount"
| extend d = parse_json(tostring(parse_json(data).Json))
| extend MountId = tostring(parse_json(data).MountId)
| extend Version = tostring(d.Version) 
| where Version == "0.2.173.2"
| where MountId <> ""
| join (
    MicrosoftGit
    | where timestamp > start
    | where name == "Microsoft.Git.GVFS.Error"
    | extend d = parse_json(tostring(parse_json(data).Json))
    | extend MountId = tostring(parse_json(data).MountId)
    | extend Exception = tostring(d.Exception)
    | extend ErrorMessage = tostring(d.ErrorMessage)
    | summarize count() by MountId) on MountId  
| join (
   MicrosoftGit
    | where timestamp > start
    | where name == "Microsoft.Git.GVFS.Mount"
    | extend d = parse_json(tostring(parse_json(data).Json))
    | extend MountId = tostring(parse_json(data).MountId)
    | extend Message = tostring(d.Message)
    | where MountId <> ""
    | where Message == "Virtual repo is ready") on MountId
```
## Failures do to user error (Still being worked on)

## Digging into a Mount Failure that happened after a clone
InitiatedByClone flag on the start of the mount process will let us know this mount process was initiated by a Clone.

We can look up the parameters that started the clone by matching up EnlistmentRoot and user_id
```
    let start = ago(10d); 
   MicrosoftGit
    | where timestamp > start
    | where name == "Microsoft.Git.GVFS.GVFSClone"
    | extend d = parse_json(tostring(parse_json(data).Json))
    | extend j = parse_json(data)
    | extend EnlistmentRoot = tostring(d.EnlistmentRoot)
    | where EnlistmentRoot like "C:\\Repos\\GVFSFunctionalTests\\enlistment\\testme7"
    | where user_id == "a:XXXXXXX-XXXXX-XXXX-XXXX-XXXXXXXXXX"
```

Errors that are outputed from Clone won't have EnlistmentRoot.  You can run a query for the user_id based on timestamp to find associated errors.  This isn't perfect though )(as Kevin suggested) since two Clones could be running at the same time for a given user.  I believe this is an unlikely scenario.  If the error is mount related we will be able to tie together errors since they have a mountId.  