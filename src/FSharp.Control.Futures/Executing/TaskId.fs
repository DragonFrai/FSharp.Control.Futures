namespace FSharp.Control.Futures.Executing.Scheduling

open System.Threading


// [ Global TaskId ]
[<Struct; StructuralComparison; StructuralEquality>]
type TaskId = TaskId of uint32

module TaskId =
    let mutable current: uint32 = 0u
    let inline generate () = Interlocked.Increment(&current) |> TaskId
