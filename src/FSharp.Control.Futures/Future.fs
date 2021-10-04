namespace FSharp.Control.Futures

open System
open System.ComponentModel
open System.Threading
open FSharp.Control.Futures.Core

// Contains the basic functions for creating and transforming `Computation`.
// If the function accepts types other than `Computation` or `Context`, then they should be placed somewhere else

type Future<'a> = Core.Future<'a>

[<RequireQualifiedAccess>]
module Future =

    open Core.Future

    /// <summary> Create the Future returned <code>Ready ()</code> when polled</summary>
    /// <returns> Future returned <code>Ready ()value)</code> when polled </returns>
    let unit =
        create (fun () -> AsyncComputation.readyUnit)

    /// <summary> Creates always pending Future </summary>
    /// <returns> always pending Future </returns>
    let inline never<'a> : Future<'a> =
        create (fun () -> AsyncComputation.never<'a>)

    let inline ready (x: 'a) : Future<'a> =
        create (fun () -> AsyncComputation.ready x)

    /// <summary> Creates the Future lazy evaluator for the passed function </summary>
    /// <returns> Future lazy evaluator for the passed function </returns>
    let inline lazy' f =
        create (fun () -> AsyncComputation.lazy' f)

    /// <summary> Creates the Future, asynchronously applies the result of the passed future to the binder </summary>
    /// <returns> Future, asynchronously applies the result of the passed future to the binder </returns>
    let inline bind binder fut =
        create (fun () -> AsyncComputation.bind (binder >> startComputation) (startComputation fut) )

    /// <summary> Creates the Future, asynchronously applies mapper to result passed Computation </summary>
    /// <returns> Future, asynchronously applies mapper to result passed Computation </returns>
    let inline map mapping fut =
        create (fun () -> AsyncComputation.map mapping (startComputation fut))

    /// <summary> Creates the Future, asynchronously applies 'f' function to result passed Computation </summary>
    /// <returns> Future, asynchronously applies 'f' function to result passed Computation </returns>
    let inline apply f fut =
        create (fun () -> AsyncComputation.apply (startComputation f) (startComputation fut))

    /// <summary> Creates the Future, asynchronously merging the results of passed Future </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Future will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    let inline merge fut1 fut2 =
        create (fun () -> AsyncComputation.merge (startComputation fut1) (startComputation fut2))

    /// <summary> Creates a Future that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Future threw an exception, the same exception will be thrown everywhere,
    /// and the other Future will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    let inline first fut1 fut2 =
        create (fun () -> AsyncComputation.first (startComputation fut1) (startComputation fut2))

    /// <summary> Creates the Future, asynchronously joining the result of passed Computation </summary>
    /// <returns> Future, asynchronously joining the result of passed Computation </returns>
    let inline join fut =
        create (fun () -> AsyncComputation.join (startComputation (map startComputation fut)))

    /// <summary> Creates a Future that returns control flow to the scheduler once </summary>
    /// <returns> Future that returns control flow to the scheduler once </returns>
    let yieldWorkflow = create AsyncComputation.yieldWorkflow

    exception FusedFutureRerunException
    /// <summary> Creates a Future that can be run only once </summary>
    /// <returns> Fused future </returns>
    let inline fuse (source: Future<'a>) : Future<'a> =
        let mutable isRun = 0 // 1 = true; 0 = false
        create (fun () ->
            if Interlocked.CompareExchange(&isRun, 1, 0) = 0
            then source.StartComputation()
            else raise FusedFutureRerunException)

    /// <summary> Creates a Future that ignore result of the passed Computation </summary>
    /// <returns> Future that ignore result of the passed Computation </returns>
    let inline ignore fut =
        create (fun () -> AsyncComputation.ignore (startComputation fut))

