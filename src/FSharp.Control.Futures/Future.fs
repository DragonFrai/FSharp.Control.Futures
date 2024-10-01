namespace FSharp.Control.Futures

open System
open System.Collections.Generic
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


[<RequireQualifiedAccess>]
module Future =

    /// <summary> Create the Future with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Future returned <code>Ready value</code> when polled </returns>
    let inline ready (value: 'a) : Future<'a> =
        upcast Futures.Ready(value)

    /// <summary> Create the Future returned <code>Ready ()</code> when polled</summary>
    /// <returns> Future returned <code>Ready ()value)</code> when polled </returns>
    let unit': Future<unit> =
        upcast Futures.ReadyUnit

    /// <summary> Creates always pending Future </summary>
    /// <returns> always pending Future </returns>
    let never<'a> : Future<'a> =
        upcast Futures.Never

    /// <summary> Creates the Future lazy evaluator for the passed function </summary>
    /// <returns> Future lazy evaluator for the passed function </returns>
    /// <remarks>The passed function will block context switching until it is executed.
    /// It is not recommended to wrap long functions if you are not sure what you are doing.</remarks>
    let inline lazy' (f: unit -> 'a) : Future<'a> =
        upcast Futures.Lazy(f)

    /// <summary> Creates the Future, asynchronously applies the result of the passed compute to the binder </summary>
    /// <returns> Future, asynchronously applies the result of the passed compute to the binder </returns>
    let inline bind (binder: 'a -> Future<'b>) (source: Future<'a>) : Future<'b> =
        upcast Futures.Bind(source, binder)

    /// <summary> Creates the Future, asynchronously applies mapper to result passed Future </summary>
    /// <returns> Future, asynchronously applies mapper to result passed Future </returns>
    let inline map (mapping: 'a -> 'b) (source: Future<'a>) : Future<'b> =
        upcast Futures.Map(source, mapping)

    /// <summary> Creates the Future, asynchronously merging the results of passed Futures </summary>
    /// <remarks> If one of the Futures threw an exception, the same exception will be thrown everywhere,
    /// and the other Futures will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    let inline merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        upcast Futures.Merge(fut1, fut2)

    /// <summary> Creates a Futures that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Futures threw an exception, the same exception will be thrown everywhere,
    /// and the other Futures will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    let inline first (fut1: Future<'a>) (fut2: Future<'a>) : Future<'a> =
        upcast Futures.First(fut1, fut2)

    /// <summary> Creates the Future, asynchronously applies 'f' function to result passed Future </summary>
    /// <returns> Future, asynchronously applies 'f' function to result passed Future </returns>
    let inline apply (futFun: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        upcast Futures.Apply(fut, futFun)

    /// <summary> Creates the Future, asynchronously joining the result of passed Future </summary>
    /// <returns> Future, asynchronously joining the result of passed Future </returns>
    let inline join (fut: Future<Future<'a>>) : Future<'a> =
        upcast Futures.Join(fut)

    /// <summary> Create a Future delaying invocation and computation of the Future of the passed creator </summary>
    /// <returns> Future delaying invocation and computation of the Future of the passed creator </returns>
    let inline delay (creator: unit -> Future<'a>) : Future<'a> =
        upcast Futures.Delay(creator)

    let inline catch (source: Future<'a>) : Future<Result<'a, exn>> =
        upcast Futures.TryWith(Futures.Map(source, Ok), fun ex -> Futures.Ready(Error ex))

    // TODO: Rename one of inspect* functions (maybe watch)
    let inline inspectM (inspector: 'a -> Future<unit>) (fut: Future<'a>) : Future<'a> =
        fut |> bind (fun x -> inspector x |> bind (fun () -> ready x))

    let inline inspect (inspector: 'a -> unit) (fut: Future<'a>) : Future<'a> =
        fut |> inspectM (fun x -> lazy' (fun () -> inspector x))

    let inline tryWith (body: Future<'a>) (handler: exn -> Future<'a>) : Future<'a> =
        upcast Futures.TryWith(body, handler)

    let inline tryFinally (body: Future<'a>) (finalizer: unit -> unit): Future<'a> =
        catch body
        |> inspect (fun _ -> do finalizer ())
        |> map (fun x -> match x with Ok r -> r | Error ex -> raise ex)

    /// <summary> Creates a Future that returns control flow to the runtime once </summary>
    /// <returns> Future that returns control flow to the runtime once </returns>
    let inline yieldWorkflow () : Future<unit> =
        upcast Futures.Yield()

    [<RequireQualifiedAccess>]
    module Seq =

        let fold (folder: 's -> 'a -> Future<'s>) (state: 's) (source: 'a seq) : Future<'s> =
            let rec foldAsyncLoop folder state (enumerator: IEnumerator<'a>) =
                if enumerator.MoveNext() then
                    bind (fun state -> foldAsyncLoop folder state enumerator) (folder state enumerator.Current)
                else
                    ready state
            delay (fun () -> foldAsyncLoop folder state (source.GetEnumerator()))

        /// <summary> Creates a future async iterated over a sequence </summary>
        /// <remarks> The generated future does not substitute implicit breakpoints,
        /// so on long iterations you should use <code>yieldWorkflow</code> </remarks>
        let iter (action: 'a -> Future<unit>) (source: 'a seq) : Future<unit> =
            fold (fun () -> action) () source

    let inline raise (source: Future<Result<'a, exn>>) : Future<'a> =
        upcast Futures.Map(source, function Ok r -> r | Error ex -> raise ex)

    /// <summary> Creates a Future that ignore result of the passed Future </summary>
    /// <returns> Future that ignore result of the passed Future </returns>
    let inline ignore (fut: Future<'a>) : Future<unit> =
        upcast Futures.Ignore(fut)

// --------
// Builder
// --------

type FutureBuilder() =

    member inline _.Return(x): Future<'a> = Future.ready x

    member inline _.Bind(ca: Future<'a>, a2cb: 'a -> Future<'b>) = Future.bind a2cb ca

    member inline _.Zero(): Future<unit> = Future.unit'

    member inline _.ReturnFrom(c: Future<'a>): Future<'a> = c

    member inline this.Combine(cu: Future<unit>, u2c: unit -> Future<'a>) = this.Bind(cu, u2c)

    member inline _.MergeSources(c1: Future<'a>, c2: Future<'b>): Future<'a * 'b> =
        Future.merge c1 c2

    member inline _.Delay(u2c: unit -> Future<'a>) = u2c

    member inline _.For(source, body) = Future.Seq.iter body source

    member inline this.While(cond: unit -> bool, body: unit -> Future<unit>): Future<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member inline _.TryWith(body: unit -> Future<'a>, handler: exn -> Future<'a>): Future<'a> =
        Future.tryWith (Future.delay body) handler

    member inline _.TryFinally(body: unit -> Future<'a>, finalizer: unit -> unit): Future<'a> =
        Future.tryFinally (Future.delay body) finalizer

    member inline _.Using<'d, 'a when 'd :> IDisposable>(disposable: 'd, body: 'd -> Future<'a>): Future<'a> =
        let body' = fun () -> body disposable
        let disposer = fun () ->
            match disposable with
            | null -> ()
            | disposable -> disposable.Dispose()
        Future.tryFinally (Future.delay body') disposer

    member inline _.Run(u2c: unit -> Future<'a>): Future<'a> = Future.delay u2c


[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()
