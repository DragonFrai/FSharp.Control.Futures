namespace FSharp.Control.Futures.Examples.Grep

open System.IO
open FSharp.Control.Futures


type Entry =
    { Line: int; Column: int; LineStr: string }

type ScanResult =
    { FilePath: string
      Result: Result<Entry, string> }

[<RequireQualifiedAccess>]
module GrepUtils =

    /// <summary>
    /// Enumerate all files by path recursive using onFind action
    /// </summary>
    let allFilesRec (root: string) (onFind: string option -> Future<unit>) = future {
        let rec scanDir (path: string) = future {
            let attrs = File.GetAttributes(path)
            if not (attrs.HasFlag(FileAttributes.Directory)) then
                do! onFind (Some path)
            else
                for file in Directory.GetFiles(path) do
                    do! onFind (Some file)
                for dir in Directory.GetDirectories(path) do
                    do! scanDir dir
                do! Future.yieldWorkflow ()
        }
        do! scanDir root
        do! onFind None
    }

    /// <summary>
    /// Enumerate indexes of pattern in string using onFind action
    /// </summary>
    let scanString (str: string) (pattern: string) (onFind: int -> Future<unit>) = future {
        let rec loop (str: string) (pattern: string) (currentIdx: int) = future {
            let entryIdx = str.IndexOf(pattern, currentIdx)
            if entryIdx = -1 then ()
            else
                do! onFind entryIdx
                do! Future.yieldWorkflow ()
                return! loop str pattern (entryIdx + 1)
        }
        return! loop str pattern 0
    }

    let scanFile (path: string) (pattern: string) (onFind: ScanResult -> Future<unit>) = future {
        let sizeLimit = 1024 * 1024 * 16
        let allowedExtension = [ "txt"; "json"; "toml"; "yml"; "yaml"; "fs"; "fsx"; "fsi"; "cs" ]

        let info = FileInfo(path)

        if info.Length > sizeLimit then
            let entry = { FilePath = path; Result = Error "File to large" }
            do! onFind entry
            return ()
        elif not (info.Extension.Length > 1 && List.contains (info.Extension.Substring(1)) allowedExtension) then
            let entry = { FilePath = path; Result = Error "Extension not allowed for scanning" }
            do! onFind entry
            return ()
        else
            use reader = new StreamReader(path)
            let rec loop (lineNumber: int) = future {
                let! line = reader.ReadLineAsync() |> Future.ofTask
                match line with
                | null -> return ()
                | line ->
                    let onFind column = future {
                        let res = { Line = lineNumber; Column = column; LineStr = line }
                        let entry = { FilePath = path; Result = Ok res }
                        do! onFind entry
                    }
                    do! scanString line pattern onFind
                    return! loop (lineNumber + 1)
            }
            return! loop 0
    }
