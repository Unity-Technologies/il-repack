Introduction
============

ILRepack is meant at replacing [ILMerge](http://www.microsoft.com/downloads/details.aspx?FamilyID=22914587-B4AD-4EAE-87CF-B14AE6A939B0&displaylang=en) / [Mono.Merge](http://evain.net/blog/articles/2006/11/06/an-introduction-to-mono-merge).

The former being ~~closed-source~~ ([now open-sourced](https://github.com/Microsoft/ILMerge)), impossible to customize, slow, resource consuming and many more.
The later being deprecated, unsupported, and based on an old version of Mono.Cecil.

This fork is using the latest Cecil, and has been updated to run on .NET 8 and to be buildable/testable/runnable cross-platform. Support for WPF, and older .NET features such as signing the assembly with a strong name, have been stripped out.

Syntax
------

A console application is available (can be used as DLL as well), using same syntax as ILMerge:
```
Syntax: ILRepack.exe [options] /out:<path> <path_to_primary> [<other_assemblies> ...]
    or: ILRepack.exe [options] /config:<path_to_json>

 - /help                displays this usage
 - /config:<path>       use multi-assembly repack mode with JSON configuration file
 - /log:<logfile>       enable logging (to a file, if given) (default is disabled)
 - /ver:M.X.Y.Z         target assembly version
 - /union               merges types with identical names into one
 - /ndebug              disables symbol file generation
 - /copyattrs           copy assembly attributes (by default only the primary assembly attributes are copied)
 - /attr:<path>         take assembly attributes from the given assembly file
 - /allowMultiple       when copyattrs is specified, allows multiple attributes (if type allows)
 - /target:kind         specify target assembly kind (library, exe, winexe supported, default is same as first assembly)
 - /targetplatform:P    specify target platform (v1, v1.1, v2, v4 supported)
 - /xmldocs             merges XML documentation as well
 - /lib:<path>          adds the path to the search directories for referenced assemblies (can be specified multiple times)
 - /internalize         sets all types but the ones from the first assembly 'internal'
 - /renameInternalized  rename all internalized types
 - /align               - NOT IMPLEMENTED
 - /closed              - NOT IMPLEMENTED
 - /allowdup:Type       allows the specified type for being duplicated in input assemblies
 - /allowduplicateresources allows to duplicate resources in output assembly (by default they're ignored)
 - /zeropekind          allows assemblies with Zero PeKind (but obviously only IL will get merged)
 - /wildcards           allows (and resolves) file wildcards (e.g. `*`.dll) in input assemblies
 - /parallel            use as many CPUs as possible to merge the assemblies
 - /pause               pause execution once completed (good for debugging)
 - /verbose             shows more logs
 - /out:<path>          target assembly path, symbol/config/doc files will be written here as well
 - <path_to_primary>    primary assembly, gives the name, version to the merged one
 - <other_assemblies>   ...

Note: for compatibility purposes, all options can be specified using '/', '-' or '--' prefix.
```

Multi-Assembly Repack Mode
------

ILRepack now supports merging multiple groups of assemblies into separate output assemblies. This is useful as a 'batch mode' when you want to do many merges, but is also supports automatic reference rewriting. Suppose you have:

```
Group A: {A1.dll, A2.dll, A3.dll} -> A.dll
Group B: {B1.dll, B2.dll, B3.dll} -> B.dll
```

And `B1.dll` references `A1.dll`.

If you just do those two merges as separate operations, then `B.dll` will end up referencing `A1.dll` still. But with this feature, `B.dll` will have any references to `A1.dll`, `A2.dll` or `A3.dll` rewritten to point at `A.dll`.

**Usage:**

Create a JSON configuration file, for example:
```json
{
  "groups": [
    {
      "name": "CoreGroup",
      "inputAssemblies": ["Core.dll", "Utilities.dll"],
      "outputAssembly": "MyApp.Core.dll"
    },
    {
      "name": "UIGroup",
      "inputAssemblies": ["UI.dll", "Controls.dll"],
      "outputAssembly": "MyApp.UI.dll"
    }
  ],
  "globalOptions": {
    "internalize": true,
    "debugInfo": true
  }
}
```

Then run:
```bash
ILRepack.exe /config:repack-config.json
```

How to build
------

Builds directly from within Visual Studio 2015, or using dotnet:

```
git clone --recursive https://github.com/gluck/il-repack.git
cd il-repack
dotnet build
```

TODO
------
  * Crash-testing
  * Add remaining features from ILMerge (closed / align)
  * Merge import process & reference fixing

DONE
------
  * Multi-assembly repack mode with reference rewriting
  * Circular dependency detection for multi-assembly mode
  * JSON-based configuration for complex merge scenarios
  * PDBs & MDBs should be merged (Thanks Simon)
  * Fixed internal method overriding public one which isn't allowed in the same assembly (Simon)
  * Attribute merge (/copyattrs)
  * XML documentation merge
  * Clean command line parameter parsing
  * Add usage / version
  * App.config merge
  * Internalizing types (Simon)
  * Target platform selection (Simon)
  * Automatic internal type renaming

Sponsoring / Donations
------
If you like this tool and want to express your thanks, you can contribute either time to the project (issue triage or pull-requests) or donate money to the Free Software Foundation.

[![Donate](https://www.gnu.org/graphics/logo-fsf.org-tiny.png)](https://my.fsf.org/donate/)
