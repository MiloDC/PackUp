# PackUp 1.3.0
PackUp is a .NET archival library, coded in F# and designed for use primarily in that language.  In addition to compression, it supports predefined edits to archived files.

#### Supported compression formats:
- **zip**
- **tar**
- "**tarzip**," i.e. a .zip file containing a tarball (.tar.gz)

The **zip** and **tarzip** formats support optional password protection.

## Usage
### The `Pack` type
The PackUp library operates on `Pack` records:
```
type Pack =
    {
        description     : string
        rootDir         : string
        compression     : Compression
        targetPath      : string
        files           : Regex list * Regex list * Regex list
        edits           : (string, (Regex, string) sequence) list
        newLine         : NewLine
    }
```
`rootDir` is the full path to the root directory of the files to be archived.

`compression` is a discriminated union thusly defined:
```
type Compression =
    | Tar
    | Zip of password : string
    | TarZip of password : string
    | NoCompression
```
Empty or null password strings are interpreted to mean no password protection.  Passing a value of `NoCompression` currently yields undefined behavior, and is therefore discouraged.

`targetPath` is a full path that informs PackUp where to write the generated archive file, minus the file extension (which will be determined by PackUp).

`files` is a tuple of regular expressions corresponding to whitelist, blacklist, and include items, respectively.  Matches are sought on all files underneath `rootDir`; if a file matches a regular expression on the whitelist, or if it matches a regular expression on the include list without also matching anything on the blacklist, then it will be added to the packed output.

`edits` is a collection of items in the format `string, (Regex, string) sequence`.  The `string` item is a file path, while the sequence of `(Regex, string)` tuples corresponds to regular expression matches and string replacements on a line-by-line basis for the given file.  For example:

`"templates/config.txt", [ (Regex "^username=.*", "username=USERNAME"); (Regex "^password=.*", "password=PASSWORD") ]`

This would result in the replacement of any line text matching the given regular expressions with their corresponding strings, in the file `templates/config.txt`.

**Note that all file paths (regular expressions and strings) in `files` and `edits` must represent paths relative to `rootDir`, _not_ full paths.**

`newLine` defines the newline string written during file edits.  The `NewLine` discriminated union comprises `CR`, `LF`, `CRLF`, and `System` (i.e. the newline string of the operating system on which the packing operation takes place).

### Packing files

To execute a packing operation on a `Pack` record, open the `PackUp` namespace and call `Pack.pack`:

`Pack.pack progressCallback pack`

`progressCallback` is an option of type `Progress -> unit` that is called regularly as packing takes place.  The `Progress` type is a discriminated union:
```
type Progress =
    | Incomplete of string * single
    | Complete of string * string
```
The first `string` value in both cases is simply `Pack.description`.  The `single` value of `Incomplete` will be some number less than `1.f`.  `Complete`, the second `string` of which is the full path of the target asset, is passed at the end of the packing operation.

## Apps
The PackUp project includes a console application that reads a JSON configuration file.

The command syntax for this console app is:

**`PackUp [OPTIONS] [CONFIG_FILE]`**

`CONFIG_FILE` defaults to `packup.json`.

Command options are:

- **`-c CONFIG`** _(multi-use)_ - Pack only the specified configuration(s) (by default, all configurations defined in the file are processed)
- **`-s BITS`** - Bitwise case-sensitivity for regular expression matching (1 = filenames, 2 = edits), default = 0
- **`-v`** - view the PackUp file only (no files will be archived)

#### Sample JSON configuration file:
```
{
	"global_files" : [
		"*.hpp",
		"*.cpp",
		"*/LICENSE",
		"*/README",
		"docs/changelist.txt",
		"-*/.vs/*",
		"-.git/*",
		"-*/bin/*",
		"-*/.obj/*"
		"-*/obj/*"
	],
	"global_edits" : {
		"templates/config.txt" : [
			"|^username=.*|username=USERNAME",
			"|^password=.*|password=PASSWORD"
		]
	},
	"configurations" : {
		"linux" : {
			"target_name" : "my-linux-project",
			"compression" : "tar",
			"files" : [
				"*/Makefile",
				"-*/*Service/*"
			],
			"edits" : {
				"*/FileSystemDaemon/options.cfg" : [
					"|^patterns=.*|patterns=*.c;*.h",
					"|^max_depth=.*|max_depth=2"
				],
				"*/TcpDaemon/options.cfg" : [
					"|^timeout_seconds=.*|timeout_seconds=60"
				]
			},
			"newline" : "LF"
		},
		"windows" : {
			"target_name" : "my_windows_project",
			"compression" : "zip",
			"password" : "P@$$wd",
			"files" : [
				"+*/3rdPartyLibrary/*/bin/*.dll",
				"*/3rdPartyLibrary/*/lib/*.lib",
				"*.sln",
				"*.vcxproj",
				"-*/*Daemon/*"
			],
			"newline" : "CRLF"
		}
	}
}
```
##### Notes on the JSON configuration file:
- The `global_files` and `global_edits` collections are processed for all configurations.
- File paths must _not_ be in regular expression syntax.  The standard wildcard notations `*` (zero or more of any characters) and `?` (any single character) are permitted.
- Files matching paths prepended with `+` are categorized as whitelist items, while `-` corresponds to the blacklist.  (See **Usage**, above.)
- `target_name` defines the file name of the target archive file.  This file will be written to the directory containing the JSON configuration file, with the appropriate extension (`.zip` or `.tar.gz`) applied.

## Dependencies
PackUp employs the following third-party libraries:

- [SharpZipLib](https://github.com/PingmanTools/SharpZipLib)

## Credits
PackUp is authored by [Milo D. Cooper](https://www.miloonline.net).
