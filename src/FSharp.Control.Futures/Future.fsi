namespace FSharp.Control.Futures

open System.ComponentModel


// Contains the basic functions for creating and transforming `Future`.
// If the function accepts types other than `Future` or `Context`, then they should be placed somewhere else

/// <summary> Current state of a Future </summary>
[<Struct; RequireQualifiedAccess>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =
    val inline onReady: f: ('a -> unit) -> x: Poll<'a> -> unit

[<AbstractClass>]
type Context =

    new : unit -> Context

    /// <summary> Awakens the future associated with this context </summary>
    abstract Wake: unit -> unit

[<Interface>]
type IFuture<'a> =
    /// <summary> Poll the state </summary>
    /// <param name="context"> Current Future context </param>
    /// <returns> Current state </returns>
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Poll : context: Context -> Poll<'a>

    /// <summary> Cancel asynchronously Future computation </summary>
    /// <remarks> Notifies internal asynchronous operations of Future cancellations. It is useless if Future is cold.  </remarks>
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel : unit -> unit

type Future<'a> = IFuture<'a>

[<RequireQualifiedAccess>]
module Future =

    [<RequireQualifiedAccess>]
    module Core =

        /// <summary> Create a Future with members from passed functions </summary>
        /// <param name="__expand_poll"> Poll body </param>
        /// <param name="__expand_cancel"> Poll body </param>
        /// <returns> Future implementations with passed members </returns>
        val inline create: __expand_poll: (Context -> Poll<'a>) -> __expand_cancel: (unit -> unit) -> Future<'a>

        /// <summary> Create a Future memoizing the first <code>Ready x</code> value
        /// with members from passed functions </summary>
        /// <param name="__expand_poll"> Poll body </param>
        /// <param name="__expand_cancel"> Poll body </param>
        /// <returns> Future implementations with passed members </returns>
        val inline createMemo: __expand_poll: (Context -> Poll<'a>) -> __expand_cancel: (unit -> unit) -> Future<'a>

        /// <summary> Call Future.Poll of passed Future </summary>
        val inline poll: context: Context -> fut: Future<'a> -> Poll<'a>


    /// <summary> Create the Future with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Future returned <code>Ready value</code> when polled </returns>
    val ready: value: 'a -> Future<'a>

    /// <summary> Create the Future returned <code>Ready ()</code> when polled</summary>
    /// <returns> Future returned <code>Ready ()value)</code> when polled </returns>
    val unit: Future<unit>

    /// <summary> Creates always pending Future </summary>
    /// <returns> always pending Future </returns>
    val never: Future<'a>

    /// <summary> Creates the Future lazy evaluator for the passed function </summary>
    /// <returns> Future lazy evaluator for the passed function </returns>
    val lazy': f: (unit -> 'a) -> Future<'a>

    /// <summary> Creates the Future, asynchronously applies the result of the passed future to the binder </summary>
    /// <returns> Future, asynchronously applies the result of the passed future to the binder </returns>
    val bind: binder: ('a -> Future<'b>) -> fut: Future<'a> -> Future<'b>

    /// <summary> Creates the Future, asynchronously applies mapper to result passed Future </summary>
    /// <returns> Future, asynchronously applies mapper to result passed Future </returns>
    val map: mapping: ('a -> 'b) -> fut: Future<'a> -> Future<'b>

    /// <summary> Creates the Future, asynchronously applies 'f' function to result passed Future </summary>
    /// <returns> Future, asynchronously applies 'f' function to result passed Future </returns>
    val apply: f: Future<'a -> 'b> -> fut: Future<'a> -> Future<'b>

    /// <summary> Creates the Future, asynchronously merging the results of passed Futures </summary>
    /// <remarks> If one of the Futures threw an exception, the same exception will be thrown everywhere,
    /// and the other Futures will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    val merge: fut1: Future<'a> -> fut2: Future<'b> -> Future<'a * 'b>

    /// <summary> Creates a Futures that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Futures threw an exception, the same exception will be thrown everywhere,
    /// and the other Futures will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    val first: fut1: Future<'a> -> fut2: Future<'a> -> Future<'a>

    /// <summary> Creates the Future, asynchronously joining the result of passed Future </summary>
    /// <returns> Future, asynchronously joining the result of passed Future </returns>
    val join: fut: Future<Future<'a>> -> Future<'a>

    /// <summary> Create a Future delaying invocation and computation of the Future of the passed creator </summary>
    /// <returns> Future delaying invocation and computation of the Future of the passed creator </returns>
    val delay: creator: (unit -> Future<'a>) -> Future<'a>

    /// <summary> Creates a Future that returns control flow to the scheduler once </summary>
    /// <returns> Future that returns control flow to the scheduler once </returns>
    val yieldWorkflow: unit -> Future<unit>

    /// <summary> Creates a Future that ignore result of the passed Future </summary>
    /// <returns> Future that ignore result of the passed Future </returns>
    val ignore: fut: Future<'a> -> Future<unit>

