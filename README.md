# PackUp 1.0.0
PackUp is a .NET Core archival library, coded in F# and designed for use primarily in that language.  In addition to compression, it supports predefined edits to archived files.

#### Supported compression formats:
- **zip**
- **tar**
- "**tarzip**," i.e. a .zip file containing a tarball (.tar.gz)

The **zip** and **tarzip** formats support optional password protection.

## Usage
The PackUp library operates on the `Pack` record type:
```
type Pack =
    {
        rootDir         : string
        globalFiles     : DirMap * DirMap
        globalEdits     : EditMap list
        platforms       : (string * Platform) seq
    }
```
`rootDir` is the full path to the root directory of the archive.


## Apps
The PackUp project includes a console application that reads a JSON configuration file.

The command syntax for this console app is:

**`PackUp [OPTIONS] CONFIG_FILE`**

Command options are:

- **`-p PLATFORM`** _(multi-use)_ - Pack only the specified platform(s) (by default, all platforms defined in the config. file are processed)
- **`-c #`** - Bitwise case-sensitivity for regular expression matching (1 = filenames, 2 = edits), default = 0
- **`-v`** - view the contents of the PackUp file only (no platforms will be archived)

#### Sample JSON configuration file:
```
{
	"version" : "1.0.0",
	"global_files" : [
		"*/*.hpp",
		"*/*.cpp",
		"./LICENSE",
		"./README",
		"docs/changelist.txt",
		"-*/.vs/*/*",
		"-.git/*/*",
		"-*/bin/*",
		"-*/obj/*"
	],
	"global_edits" : {
		"templates/config.txt" : [
			"|^username=.*|username=USERNAME",
			"|^password=.*|password=PASSWORD"
		]
	},
	"platforms" : {
		"linux" : {
			"compression" : "tar",
			"target_name" : "my-project-linux",
			"files" : [
				"*/Makefile",
				"-Service/*"
			],
			"edits" : {
				"*/FileSystemDaemon/options.cfg" : [
					"|^patterns=.*|patterns=*.c;*.h",
					"|^max_depth=.*|max_depth=2"
				],
				"*/TcpDaemon/options.cfg" : [
					"|^timeout_seconds=.*|timeout_seconds=60"
				]
			}
		},
		"windows" : {
			"compression" : "zip",
			"target_name" : "my_project_windows",
			"password" : "P@$$wd",
			"files" : [
				"*/*.sln",
				"*/*.vcxproj",
				"-*/*Daemon/*"
			]
		}
	}
}
```
##### Notes on the JSON configuration file:
- `version` indicates the minimum version of PackUp required for the proper interpretation of the configuration.
- File paths are relative to the directory containing the JSON configuration file.  Wildcard notations `*` (zero or more of any characters) and `?` (any single character) are permitted.
- File paths must have at least one forward slash (`/`).  For files in the same directory as the configuration file, use `./filename`.
- Files matching paths prepended with `-` will not be archived.
- The syntax for `edits` and `global_edits` values is a collection of entries in the format: `"FILE_PATH" : [ "REGEX_REPLACEMENT", "..." ]`. Edits to a file are made on a per-line basis.  The first character in the `REGEX_REPLACEMENT` defines the delimiter between the regular expression and the string that will serve as a replacement in the event of a match. For example, for the  entry `"templates/config.txt" : [ "|^password=.*|password=PASSWORD" ]`, a regular expression match on `^password=.*` will be replaced with `password=PASSWORD` for every line in any file wth a path matching `templates/config.txt`.
- The `global_files` and `global_edits` collections are processed for all platforms.
- `target_name` defines the file name of the target archive file.  This file will be written to the directory conatining the JSON configuration file, with the appropriate extension (`.zip` or `.tar.gz`) applied.

## Dependencies
PackUp employs the following third-party libraries:

- [SharpZipLib](https://github.com/PingmanTools/SharpZipLib)
- [Json.NET](https://www.newtonsoft.com/json)

## Credits
PackUp is authored by [Milo D. Cooper](https://www.miloonline.net).
