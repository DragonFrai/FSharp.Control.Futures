
// Includes an extension of the base Future methods,
// which are also intended for integration with the base types BCL and FSharp.Core.
// (Excluding things like system timers and potential OS interactions)

[<AutoOpen>]
module FSharp.Control.Futures.FutureExt

open System
open System.Collections.Generic
open System.Threading
open FSharp.Control.Futures.Core


module Future =

    let inline catch (source: Future<'a>) : Future<Result<'a, exn>> =
        Future.create (fun () -> AsyncComputation.catch (Future.runComputation source))

    let inline sleep (dueTime: TimeSpan) =
        Future.create (fun () -> AsyncComputation.sleep dueTime)

    let inline sleepMs (milliseconds: int) =
        let dueTime = TimeSpan.FromMilliseconds(float milliseconds)
        sleep dueTime

    /// Spawn a Future on current thread and synchronously waits for its Ready
    /// The simplest implementation of the Future scheduler.
    /// Equivalent to `(Scheduler.spawnOn anyScheduler).Join()`,
    /// but without the cost of complex general purpose scheduler synchronization
    let runSync (fut: Future<'a>) : 'a =
        // Here you can directly call the Raw representation of the Future,
        // since the current thread already represents the computation context
        AsyncComputation.runSync (Future.runComputation fut)

    [<RequireQualifiedAccess>]
    module Seq =

        /// <summary> Creates a future iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>iterAsync</code> and <code>yieldWorkflow</code> </remarks>
        let inline iter (seq: 'a seq) (body: 'a -> unit) =
            Future.create (fun () -> AsyncComputation.Seq.iter seq body)

        /// <summary> Creates a future async iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>yieldWorkflow</code> </remarks>
        let inline iterAsync (source: 'a seq) (body: 'a -> Future<unit>) =
            Future.create (fun () -> AsyncComputation.Seq.iterAsync source (body >> Future.runComputation))


