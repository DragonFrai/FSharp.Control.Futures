namespace FSharp.Control.Futures.Executing.Scheduling

open System.Threading


// [ Global TaskId ]
[<Struct; StructuralComparison; StructuralEquality>]
type FutureTaskId =
    TaskId of uint32
    with member inline this.Inner = let (TaskId tid) = this in tid

module FutureTaskId =
    let mutable current: uint32 = 0u
    let inline generate () = Interlocked.Increment(&current) |> TaskId
