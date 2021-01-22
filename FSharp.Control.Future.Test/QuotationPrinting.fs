module FSharp.Control.Future.Test.QuotationPrinting

open FSharp.Quotations
open System.Text


module Line =
    let appendIndent i s = String.replicate i " " + s
    let ofTokens (ts: string seq) = System.String.Join("", ts)

module Lines =
    let appendIndent i = List.map (Line.appendIndent i)
    let toStr (lines: string seq) = System.String.Join("\n", lines)
    let ofStr (s: string) = s.Split('\n') |> Array.toList


module rec Format =

    open System
    open System.Reflection
    open Microsoft.FSharp.Reflection
    
    let var' (var: Var) =
        var.Name
    
    let value' (value: obj) (typ: Type) =
        string value
    
    let propertyGet' (expr: Expr option) (propInfo: PropertyInfo) (argExprs: Expr list) =
        propInfo.Name
    
    let let' (var: Var) (expr1: Expr) (expr2: Expr) =
        Lines.toStr [
//            $"let {var.Name} ="
//            yield! expr' expr1 |> Lines.appendIndent 4
//            "in"
//            yield! expr' expr2 |> Lines.appendIndent 4
            $"let {var.Name} ="
            yield! expr' expr1 |> Lines.ofStr |> Lines.appendIndent 4
            expr' expr2
        ]
    
    let lambda' (param: Var) (body: Expr) =
        Lines.toStr [
//            $"fun ({param.Name}: {string param.Type}) ->"
            $"fun {param.Name} ->"
            yield! expr' body |> Lines.ofStr |> Lines.appendIndent 4
        ]
    
    let call' (caller: Expr option) (methodInfo: MethodInfo) (exprList: Expr list) =
        Lines.toStr [
            Line.ofTokens [
                match caller with
                | Some caller -> expr' caller
                | None -> $"{methodInfo.DeclaringType.Name}"
                $".{methodInfo.Name}("
            ]
            for expr in exprList do
                expr' expr
                |> Lines.ofStr
                |> Lines.appendIndent 4
                |> Lines.toStr
                |> fun s -> s + ","
            ")"
        ]
    
    let newObject' (info: ConstructorInfo) (argExprs: Expr list) =
        Lines.toStr [
            info.DeclaringType.Name + "("
            for expr in argExprs do
                expr' expr
                |> Lines.ofStr
                |> Lines.appendIndent 4
                |> Lines.toStr
                |> fun s -> s + ","
            ")"
        ]
    
    let newUnionCase (info: UnionCaseInfo) (exprs: Expr list) =
        Lines.toStr [
            info.Name + "("
            for expr in exprs do
                expr' expr
                |> Lines.ofStr
                |> Lines.appendIndent 4
                |> Lines.toStr
                |> fun s -> s + ","
            ")"
        ]
    
    let application' (expr1: Expr) (expr2: Expr) =
        Lines.toStr [
            expr' expr1
            "<|"
            yield!
                expr' expr2
                |> Lines.ofStr
                |> Lines.appendIndent 4
        ]
    
    let sequential' (expr1: Expr) (expr2: Expr) =
        Lines.toStr [
            yield!
                expr' expr1
                |> Lines.ofStr
                |> List.rev
                |> function [] -> [] | head :: tail -> head + ";" :: tail
                |> List.rev
            expr' expr2
        ]
    
    let corce' (expr: Expr) (typ: Type) =
        let sexpr = expr' expr
        $"{sexpr} :!> {typ.Name}"
    
    
    let expr' (expr: Expr) =
        match expr with
        | Patterns.Application(expr1, expr2) -> Format.application' expr1 expr2
        | Patterns.Sequential(expr1, expr2) -> Format.sequential' expr1 expr2
        | Patterns.Coerce(expr, typ) -> Format.corce' expr typ            
        | Patterns.NewUnionCase (info, args) -> Format.newUnionCase info args
        | Patterns.NewObject (info, argExprs) -> Format.newObject' info argExprs
        | Patterns.Call(caller, methodInfo, exprList) -> Format.call' caller methodInfo exprList
        | Patterns.Lambda(param, body) -> Format.lambda' param body
        | Patterns.Let(var, expr1, expr2) -> Format.let' var expr1 expr2
        | Patterns.PropertyGet(expr, propInfo, argExprs) -> Format.propertyGet' expr propInfo argExprs
        | Patterns.Value(value, typ) -> Format.value' value typ
        | Patterns.Var(var) -> Format.var' var
        | _ -> "<UNIMPL>"



//let expr =
//    <@
//        async {
//            printfn "Start"
//            do! Async.Sleep(1000)
//            let x = 6
//            let! results = Async.Parallel [
//                async { return 1 }
//                async { return 2 }
//            ]
//            return x //Array.sum results
//        }
//    @>
//
//let sexpr1 = printfn "%A" expr
//printfn "\n"
//printfn "<---- std ----<"
//printf $"{sexpr1}"
//printfn "%%"
//printfn ">---- std ---->"
//
//let sexpr2 = Format.expr' expr
//printfn "<---- my ----<"
//printfn $"{sexpr2}"
//printfn "%%"
//printfn ">---- my ---->"