[<AutoOpen>]
module Utils

open FSharp.Control.Futures


let nonAwakenedContext: Context = { new Context() with member _.Wake() = invalidOp "Context was wake" }
let mockContext: Context = { new Context() with member _.Wake() = do () }
let mockContextWithWake (wake: unit -> unit) = { new Context() with member _.Wake() = wake () }
