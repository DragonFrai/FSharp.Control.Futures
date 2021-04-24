namespace FSharp.Control.Futures

open System.ComponentModel


// Contains the basic functions for creating and transforming `Computation`.
// If the function accepts types other than `Computation` or `Context`, then they should be placed somewhere else

/// <summary> Current state of a Computation </summary>
[<Struct; RequireQualifiedAccess>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =
    val inline isReady: x: (Poll<'a>) -> bool
    val inline isPending: x: (Poll<'a>) -> bool
    val inline onReady: f: ('a -> unit) -> x: Poll<'a> -> unit
    val inline bind: binder: ('a -> Poll<'b>) -> x: Poll<'a> -> Poll<'b>
    val inline bindPending: binder: (unit -> Poll<'a>) -> x: Poll<'a> -> Poll<'a>
    val inline map: f: ('a -> 'b) -> x: (Poll<'a>) -> Poll<'b>


/// <summary> The context of the running computation.
/// Allows the computation to signal its ability to move forward (awake) through the Wake method </summary>
[<AbstractClass>]
type Context =
    new: unit -> Context
    /// <summary> Awakens the computation associated with this context </summary>
    abstract Wake: unit -> unit

[<Interface>]
type IComputation<'a> =
    /// <summary> Poll the state </summary>
    /// <param name="context"> Current Computation context </param>
    /// <returns> Current state </returns>
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Poll : context: Context -> Poll<'a>

    /// <summary> Cancel asynchronously Computation computation </summary>
    /// <remarks> Notifies internal asynchronous operations of Computation cancellations. It is useless if Computation is cold.  </remarks>
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel : unit -> unit

[<RequireQualifiedAccess>]
module Computation =

    /// <summary> Create a Computation with members from passed functions </summary>
    /// <param name="__expand_poll"> Poll body </param>
    /// <param name="__expand_cancel"> Poll body </param>
    /// <returns> Computation implementations with passed members </returns>
    val inline create: __expand_poll: (Context -> Poll<'a>) -> __expand_cancel: (unit -> unit) -> IComputation<'a>

    /// <summary> Create a Computation memoizing the first <code>Ready x</code> value
    /// with members from passed functions </summary>
    /// <param name="__expand_poll"> Poll body </param>
    /// <param name="__expand_cancel"> Poll body </param>
    /// <returns> Computation implementations with passed members </returns>
    val inline createMemo: __expand_poll: (Context -> Poll<'a>) -> __expand_cancel: (unit -> unit) -> IComputation<'a>

    /// <summary> Call Computation.Poll of passed Computation </summary>
    val inline poll: context: Context -> comp: IComputation<'a> -> Poll<'a>

    val inline cancel: comp: IComputation<'a> -> unit

    val inline cancelNullable: comp: IComputation<'a> -> unit


    /// <summary> Create the Computation with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Computation returned <code>Ready value</code> when polled </returns>
    val ready: value: 'a -> IComputation<'a>

    /// <summary> Create the Computation returned <code>Ready ()</code> when polled</summary>
    /// <returns> Computation returned <code>Ready ()value)</code> when polled </returns>
    val unit: IComputation<unit>

    /// <summary> Creates always pending Computation </summary>
    /// <returns> always pending Computation </returns>
    val never<'a> : IComputation<'a>

    /// <summary> Creates the Computation lazy evaluator for the passed function </summary>
    /// <returns> Computation lazy evaluator for the passed function </returns>
    val lazy': f: (unit -> 'a) -> IComputation<'a>

    /// <summary> Creates the Computation, asynchronously applies the result of the passed compure to the binder </summary>
    /// <returns> Computation, asynchronously applies the result of the passed compure to the binder </returns>
    val bind: binder: ('a -> IComputation<'b>) -> comp: IComputation<'a> -> IComputation<'b>

    /// <summary> Creates the Computation, asynchronously applies mapper to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies mapper to result passed Computation </returns>
    val map: mapping: ('a -> 'b) -> comp: IComputation<'a> -> IComputation<'b>

    /// <summary> Creates the Computation, asynchronously applies 'f' function to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies 'f' function to result passed Computation </returns>
    val apply: f: IComputation<'a -> 'b> -> comp: IComputation<'a> -> IComputation<'b>

    /// <summary> Creates the Computation, asynchronously merging the results of passed Computations </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    val merge: comp1: IComputation<'a> -> comp2: IComputation<'b> -> IComputation<'a * 'b>

    /// <summary> Creates a Computations that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    val first: comp1: IComputation<'a> -> comp2: IComputation<'a> -> IComputation<'a>

    /// <summary> Creates the Computation, asynchronously joining the result of passed Computation </summary>
    /// <returns> Computation, asynchronously joining the result of passed Computation </returns>
    val join: comp: IComputation<IComputation<'a>> -> IComputation<'a>

    /// <summary> Create a Computation delaying invocation and computation of the Computation of the passed creator </summary>
    /// <returns> Computation delaying invocation and computation of the Computation of the passed creator </returns>
    val delay: creator: (unit -> IComputation<'a>) -> IComputation<'a>

    /// <summary> Creates a Computation that returns control flow to the scheduler once </summary>
    /// <returns> Computation that returns control flow to the scheduler once </returns>
    val yieldWorkflow: unit -> IComputation<unit>

    /// <summary> Creates a Computation that ignore result of the passed Computation </summary>
    /// <returns> Computation that ignore result of the passed Computation </returns>
    val ignore: comp: IComputation<'a> -> IComputation<unit>

// --------
// Future
// --------

[<Struct; NoComparison; NoEquality>]
type Future<'a> =
    { Raw: unit -> IComputation<'a> }

[<RequireQualifiedAccess>]
module Future =

    val inline create: __expand_creator: (unit -> IComputation<'a>) -> Future<'a>
    val inline raw: fut: Future<'a> -> (unit -> IComputation<'a>)
    val inline runRaw: fut: Future<'a> -> IComputation<'a>

    /// <summary> Create the Future with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Future returned <code>Ready value</code> when polled </returns>
    val inline ready: value: 'a -> Future<'a>

    /// <summary> Create the Future returned <code>Ready ()</code> when polled</summary>
    /// <returns> Future returned <code>Ready ()value)</code> when polled </returns>
    val unit: Future<unit>

    /// <summary> Creates always pending Future </summary>
    /// <returns> always pending Future </returns>
    val inline never<'a> : Future<'a>

    /// <summary> Creates the Future lazy evaluator for the passed function </summary>
    /// <returns> Future lazy evaluator for the passed function </returns>
    val inline lazy': f: (unit -> 'a) -> Future<'a>

    /// <summary> Creates the Future, asynchronously applies the result of the passed future to the binder </summary>
    /// <returns> Future, asynchronously applies the result of the passed future to the binder </returns>
    val inline bind: binder: ('a -> Future<'b>) -> fut: Future<'a> -> Future<'b>

    /// <summary> Creates the Future, asynchronously applies mapper to result passed Computation </summary>
    /// <returns> Future, asynchronously applies mapper to result passed Computation </returns>
    val inline map: mapping: ('a -> 'b) -> fut: Future<'a> -> Future<'b>

    /// <summary> Creates the Future, asynchronously applies 'f' function to result passed Computation </summary>
    /// <returns> Future, asynchronously applies 'f' function to result passed Computation </returns>
    val inline  apply: f: Future<'a -> 'b> -> fut: Future<'a> -> Future<'b>

    /// <summary> Creates the Future, asynchronously merging the results of passed Future </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Future will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    val inline merge: fut1: Future<'a> -> fut2: Future<'b> -> Future<'a * 'b>

    /// <summary> Creates a Future that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Future threw an exception, the same exception will be thrown everywhere,
    /// and the other Future will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    val inline first: fut1: Future<'a> -> fut2: Future<'a> -> Future<'a>

    /// <summary> Creates the Future, asynchronously joining the result of passed Computation </summary>
    /// <returns> Future, asynchronously joining the result of passed Computation </returns>
    val inline join: fut: Future<Future<'a>> -> Future<'a>

//    /// <summary> Create a Future delaying invocation and computation of the Future of the passed creator </summary>
//    /// <returns> Future delaying invocation and computation of the Future of the passed creator </returns>
//    val delay: creator: (unit -> Future<'a>) -> Future<'a>

    /// <summary> Creates a Future that returns control flow to the scheduler once </summary>
    /// <returns> Future that returns control flow to the scheduler once </returns>
    val yieldWorkflow: Future<unit>

    /// <summary> Creates a Future that ignore result of the passed Computation </summary>
    /// <returns> Future that ignore result of the passed Computation </returns>
    val inline ignore: fut: Future<'a> -> Future<unit>

