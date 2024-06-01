namespace FSharp.Control.Futures.Executing
//
// open System.Collections.Generic
// open System.Linq.Expressions
// open System.Runtime.CompilerServices
// open System.Threading
// open FSharp.Control.Futures
// open FSharp.Control.Futures.Internals
//
//
// module Constants =
//     let [<Literal>] MinimalTrimDelta : int = 1024 // 4кб для int
//
//
//
// // [<Struct; NoComparison; StructuralEquality>]
// // type TaskId = TaskId of int
// //     with member this.Inner() : int = let (TaskId x) = this in x
//
// type SingleThreadScheduler() =
//     let syncObj = obj
//     let mutable disposeCancellationToken = CancellationToken()
//
//     let mutable tasks = List<ISchedulerTask>()
//     let mutable pollingQueue = Queue<ISchedulerTask>()
//
//
//
// module SingleThreadScheduler =
//     ()
