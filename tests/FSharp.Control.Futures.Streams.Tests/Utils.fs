[<AutoOpen>]
module Utils

open FSharp.Control.Futures


let nonAwakenedContext: IContext =
    { new IContext with
        member _.Wake() = invalidOp "Context was wake"
        member _.Scheduler = None }
let mockContext: IContext =
    { new IContext with
        member _.Wake() = do ()
        member _.Scheduler = None }
let mockContextWithWake (wake: unit -> unit) =
    { new IContext with
        member _.Wake() = wake ()
        member _.Scheduler = None }
