//
// RFC - https://github.com/fsharp/fslang-design/blob/master/RFCs/FS-1087-resumable-code-and-task-builder.md#specifying-resumable-code
// Signatures - https://github.com/dotnet/fsharp/blob/feature/tasks/src/fsharp/FSharp.Core/tasks.fsi
// Implementation - https://github.com/dotnet/fsharp/blob/feature/tasks/src/fsharp/FSharp.Core/tasks.fs
//

module FSharp.Control.Futures.ResumableStateMachineFutureBuilder

open System.Runtime.CompilerServices

[<AutoOpen>]
module private _Placeholder =
    module FSharp =
        module Core =
            module CompilerServices =
                type MoveNextMethod<'Template> = delegate of byref<'Template> -> unit
                type SetMachineStateMethod<'Template> = delegate of byref<'Template> * IAsyncStateMachine -> unit
                type AfterMethod<'Template, 'Result> = delegate of byref<'Template> -> 'Result
                module StateMachineHelpers =
                    let __useResumableStateMachines<'T> : bool = failwith ""
                    let __resumableEntry () : int option = failwith ""
                    let __resumeAt (programLabel: int) : 'T = failwith ""
                    let __resumableStateMachine<'T> (stateMachineSpecification: 'T) : 'T = failwith ""
                    let __resumableStateMachineStruct<'Template, 'Result>
                        (moveNextMethod: MoveNextMethod<'Template>)
                        (setMachineStateMethod: SetMachineStateMethod<'Template>)
                        (afterMethod: AfterMethod<'Template, 'Result>)
                        : 'Result = failwith ""

open FSharp.Core.CompilerServices.StateMachineHelpers


[<Struct; NoEquality; NoComparison>]
type FutureStateMachine<'T> =

    [<DefaultValue(false)>]
    val mutable Result : Future<'T>

    [<DefaultValue(false)>]
    val mutable ResumptionPoint : int

    static member Run(sm: byref<'K> when 'K :> IAsyncStateMachine) = sm.MoveNext()

    interface IAsyncStateMachine with
        member this.MoveNext() = failwith "No dynamic impl"
        member this.SetStateMachine(sm: IAsyncStateMachine) = failwith "No dynamic impl"


type FutureCode<'T> = delegate of byref<FutureStateMachine<'T>> -> unit


type FutureBuilder() =

    member inline _.Return(x: 'a): FutureCode<'a> =
        FutureCode(fun sm ->
            sm.Result <- Future.ready x
        )

    member inline _.Bind(x: Future<'a>, __expand_binder: 'a -> FutureCode<'b>): FutureCode<'b> =
        FutureCode(fun sm ->

            ()
        )


module FutureBuilderImpl =
    let future = FutureBuilder()


module __ =

    ()
