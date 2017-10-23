# The GVFS Protocol (v1)

The GVFS network protocol consists of four operations on three endpoints. In summary:
* `GET /gvfs/objects/{objectId}`
  * Provides a single object in loose-object format
* `POST /gvfs/objects`
  * Provides one or more objects in packfile or streaming loose object format
* `GET /gvfs/prefetch[?lastPackTimestamp={secondsSinceEpoch}]`
  * Provides one or more packfiles of non-blobs and optionally packfile indexes in a streaming format
* `POST /gvfs/sizes`
  * Provides the uncompressed, undeltified size of one or more objects
* `GET /gvfs/config`
  * Provides server-set client configuration options

# `GET /gvfs/objects/{objectId}`
Will return a single object in compressed loose object format, which can be directly
written to `.git/xx/yyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy` if desired. The request/response looks
similar to the "Dumb Protocol" as described [here](https://git-scm.com/book/en/v2/Git-Internals-Transfer-Protocols).

# `POST /gvfs/objects`
Will return multiple objects, possibly more than the client requested based on request parameters.

The request consists of a JSON body with the following format:
```
{
    "objectIds" : [ {JSON array of SHA-1 object IDs, as strings} ],
    "commitDepth" : {positive integer}
}
```

For example,
```
{
    "objectIds" : [
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
    ],
    "commitDepth" : 1
}
```

## `Accept: application/x-git-packfile` (the default)

If
* An `Accept` header of  `application/x-git-packfile` is specified, or 
* No `Accept` header is specified

then a git packfile, indexable via `index-pack`, will be returned to the client.

If `objectIds` includes a `commit`, then all `tree`s recursively referenced by that commit are also returned. 
If any other object type is requested (`tree`, `blob`, or `tag`), then only that object will be returned.

`commitDepth` - if the requested object is a `commit`, all parents up to `n` levels deep will be returned, along
with all their trees as previously described. Does not include any `blob`s.

## `Accept: application/x-gvfs-loose-objects`

**NOTE**: This format is currently only supposed by the cache server, not by VSTS.

To enable scenarios where multiple objects are required, but less overhead would be incurred by using pre-existing
loose objects (e.g. on a caching proxy), an alternative, packfile-like response format that contains loose objects 
is also supported.

To receive objects in this format, the client **MUST** supply an `Accept` header of `application/x-gvfs-loose-objects` 
to the `POST /gvfs/objects` endpoint. Otherwise, the response format will be `application/x-git-packfile`.

This format will **NOT** perform any `commit` to `tree` expansion, and will return an error if a `commitDepth`
greater than `1` is supplied. Said another way, this `Accept`/return type has no concept of "implicitly-requested"
objects.

### Version 1
* Integers are signed and little-endian, unless otherwise specified
* Byte offset 0 is the first byte in the file
* Index offset 0 is the first byte in the first element of an array
* `num_objects` represents the variable number of objects in the file/response

```
Count            Size (bytes)    Chunk Description

HEADER
                +-------------------------------------------------------------------------------+
1               |          5 | UTF-8 encoded 'GVFS '                                            |
                |          1 | Unsigned byte version number. Currently, 1.                      |
                +-------------------------------------------------------------------------------+

OBJECT CONTENT
                +-------------------------------------------------------------------------------+
num_objects     |         20 | SHA-1 ID of the object.                                          |
                |          8 | Signed-long length of the object.                                |
                |   variable | Compressed, raw loose object content.                            |
                +-------------------------------------------------------------------------------+

TRAILER
                +-------------------------------------------------------------------------------+
1               |         20 | Zero bytes                                                       |
                +-------------------------------------------------------------------------------+
```

# `GET /gvfs/prefetch[?lastPackTimestamp={secondsSinceEpoch}]`

To enable the reuse of already-existing packfiles and indexes, a custom format for transmitting these files
is supported. The `prefetch` endpoint will return one or more packfiles of **non-blob** objects.  

If the optional `lastPackTimestamp` query parameter is supplied, only packs created by the server
after the specific Unix epoch time (approximately, ±10 minutes or so) will be returned. Generally, these packs 
will contain only objects introduced to the repository after that UTC-based timestamp, but will not contain
**all** objects introduced after that timestamp.

A media-type of `application/x-gvfs-timestamped-packfiles-indexes` will be returned from this endpoint.

## Response format

* Integers are signed and little-endian, unless otherwise specified
* Byte offset 0 is the first byte in the file
* Index offset 0 is the first byte in the first element of an array
* `num_packs` represents the variable number of packs in the file/response

### Version 1

```
Count            Size (bytes)    Chunk Description

HEADER
                +-------------------------------------------------------------------------------+
1               |          5 | UTF-8 encoded 'GPRE '                                            |
                |          1 | Unsigned byte version number. Currently, 1.                      |
                +-------------------------------------------------------------------------------+

CONTENT

                +-------------------------------------------------------------------------------+
1               |          2 | Unsigned short number of packs. `num_packs`.                     |
                +-------------------------------------------------------------------------------+

                +-------------------------------------------------------------------------------+
num_packs       |          8 | Signed-long pack timestamp in seconds since UTC epoch.           |
                |          8 | Signed-long length of the pack.                                  |
                |          8 | Signed-long length of the pack index. -1 indicates no index.     |
                |   variable | Pack contents.                                                   |
                |   variable | Pack index contents.                                             |
                +-------------------------------------------------------------------------------+
```

Packs **MUST** be sent in increasing `timestamp` order. In the case of a failed connection, this allows the 
client to keep the packs it received successfully and "resume" by sending the highest completed timestamp.

# `POST /gvfs/sizes`
Will return the uncompressed, undeltified length of the requested objects in JSON format.

The request consists of a JSON body with the following format:
```
[ {JSON array of SHA-1 object IDs, as strings} ]
```

For example, a request of:
```
[
    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
]
```

will result in a a response like:
```
[
    {
        "Id" : "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "Size" : 123
    },
    {
        "Id" : "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        "Size" : 456
    }
]
```

# `GET /gvfs/config`
This optional endpoint will return all server-set GVFS client configuration options. It currently
provides:

* A set of allowed GVFS client version ranges, in order to block older clients from running in 
certain scenarios. For example, a data corruption bug may be found and encouraging clients to 
avoid that version is desirable.
* A list of available cache servers, each describing their url and default-ness with a friendly name
that users can use to inform which cache server to use. Note that the names "None" and "User Defined" 
are reserved by GVFS. Any caches with these names may cause undefined behavior in the GVFS client.

An example response is provided below. Note that the `null` `"Max"` value is only allowed for the last
(or greatest) range, since it logically excludes greater version numbers from having an effect.
```
{
	"AllowedGvfsClientVersions": [{
		"Max": {
			"Major": 0,
			"Minor": 4,
			"Build": 0,
			"Revision": 0
		},
		"Min": {
			"Major": 0,
			"Minor": 2,
			"Build": 0,
			"Revision": 0
		}
	}, {
		"Max": {
			"Major": 0,
			"Minor": 5,
			"Build": 0,
			"Revision": 0
		},
		"Min": {
			"Major": 0,
			"Minor": 4,
			"Build": 17009,
			"Revision": 1
		}
	}, {
		"Max": null,
		"Min": {
			"Major": 0,
			"Minor": 5,
			"Build": 16326,
			"Revision": 1
		}
	}],
	"CacheServers": [{
		"Url": "https://redmond-cache-machine/repo-id",
		"Name": "Redmond",
		"GlobalDefault": true
	}, {
		"Url": "https://dublin-cache-machine/repo-id",
		"Name": "Dublin",
		"GlobalDefault": false
	}]
}
```
