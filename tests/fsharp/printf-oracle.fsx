// Compares NetWasm.FSharp.Core's printf engine with FSharp.Core's on CoreCLR.
//   dotnet build src/NetWasm.FSharp.Core -c Release
//   dotnet fsi tests/fsharp/printf-oracle.fsx
#r "../../src/NetWasm.FSharp.Core/bin/Release/netstandard2.1/NetWasm.FSharp.Core.dll"
open NetWasm.FSharp.Core

type Point = { X: int; Y: int }
type Shape = Circle of float | Square

let mutable failures = 0
let mutable total = 0
let check (name: string) (expected: string) (actual: string) =
    total <- total + 1
    if expected <> actual then
        failures <- failures + 1
        printfn "DIFF %-28s expected %A got %A" name expected actual

// Each case: the real sprintf, and ours over the same format object.
let inline case1 (fmt: PrintfFormat<'a -> string, unit, string, string>) (a: 'a) =
    check fmt.Value (sprintf fmt a) (Printf.Sprintf1<'a> fmt a)
let inline case2 (fmt: PrintfFormat<'a -> 'b -> string, unit, string, string>) (a: 'a) (b: 'b) =
    check fmt.Value (sprintf fmt a b) (Printf.Sprintf2<'a, 'b> fmt a b)
let inline case3 (fmt: PrintfFormat<'a -> 'b -> 'c -> string, unit, string, string>) (a: 'a) (b: 'b) (c: 'c) =
    check fmt.Value (sprintf fmt a b c) (Printf.Sprintf3<'a, 'b, 'c> fmt a b c)

check "literal" (sprintf "hello %% world") (Printf.Sprintf0 "hello %% world")
for v in [ 0; 1; -1; 42; -42; System.Int32.MaxValue; System.Int32.MinValue ] do
    case1 "%d" v; case1 "%i" v; case1 "%5d" v; case1 "%-5d|" v; case1 "%05d" v; case1 "%+d" v; case1 "% d" v
    case1 "%x" v; case1 "%X" v; case1 "%o" v; case1 "%u" v; case1 "%08x" v; case1 "%B" v; case1 "%A" v; case1 "%O" v
for v in [ 0L; -5L; System.Int64.MaxValue; System.Int64.MinValue ] do
    case1 "%d" v; case1 "%x" v; case1 "%u" v; case1 "%A" v
case1 "%d" 7uy; case1 "%x" 255uy; case1 "%A" 7uy; case1 "%d" -7y; case1 "%x" -1y; case1 "%A" -7y
case1 "%d" 7s; case1 "%x" -1s; case1 "%A" 7s; case1 "%A" 7us; case1 "%A" 7u; case1 "%x" 4000000000u
case1 "%A" 7UL; case1 "%d" 18446744073709551615UL; case1 "%A" 3n; case1 "%A" 3un; case1 "%d" 3n
for v in [ 0.0; 1.0; -1.5; 3.14159265358979; 1e10; 1.5e-7; 123456789.123; -0.0; nan; infinity; -infinity ] do
    case1 "%f" v; case1 "%.2f" v; case1 "%e" v; case1 "%E" v; case1 "%g" v; case1 "%G" v; case1 "%10.3f" v
    case1 "%-10.3f|" v; case1 "%010.3f" v; case1 "%+.1f" v; case1 "%A" v; case1 "%O" v; case1 "%.0f" v; case1 "%.3e" v
for v in [ 1.5f; -2.25f; 3.14159f; 1e10f ] do
    case1 "%f" v; case1 "%A" v; case1 "%g" v
for v in [ 1.5M; -2.25M; 100M; 0.1M ] do
    case1 "%M" v; case1 "%A" v; case1 "%.3M" v; case1 "%f" v; case1 "%O" v
for v in [ "abc"; ""; null; "with \"quotes\"" ] do
    case1 "%s" v; case1 "%10s|" v; case1 "%-10s|" v; case1 "%A" v; case1 "%O" v
case1 "%c" 'x'; case1 "%A" 'x'; case1 "%3c" 'y'
case1 "%b" true; case1 "%b" false; case1 "%A" true; case1 "%6b|" true
case1 "%A" (); case1 "%O" ()
case1 "%A" (Some 3); case1 "%A" (None: int option); case1 "%A" (Some "x"); case1 "%O" (Some 3)
case1 "%A" [ 1; 2; 3 ]; case1 "%A" ([]: int list); case1 "%A" [| 1; 2 |]; case1 "%A" [| "a" |]; case1 "%A" ([||]: int[])
case1 "%A" (1, "two"); case1 "%A" (1, 2.5, 'c'); case1 "%A" [ (1, 2); (3, 4) ]
case1 "%A" [ Some 1; None ]; case1 "%A" (seq { 1 .. 10 }); case1 "%A" [ 1 .. 150 ]
case1 "%A" { X = 1; Y = 2 }; case1 "%A" (Circle 1.0); case1 "%A" Square; case1 "%O" { X = 1; Y = 2 }
case1 "%A" (Map.ofList [ 1, "a" ]); case1 "%A" (set [ 1; 2 ]); case1 "%A" (System.Text.StringBuilder "sb")
case2 "%d %s" 1 "x"; case2 "%*d|" 6 42; case2 "%-*d|" 6 42; case3 "%*.*f|" 10 2 3.14159
case2 "%a!" (fun () (x: int) -> string (x * 2)) 21
case1 "%t!" (fun () -> "text")
case2 "%s=%A" "k" [ 1 ]
let x = 42 in check "interp" $"x={x} %d{x} %5d{x} %A{[x]}" (Printf.Sprintf0 $"x={x} %d{x} %5d{x} %A{[x]}")
let y = 3.14159 in check "interp-fmt" $"{y:N2}|{y,8:F1}|" (Printf.Sprintf0 $"{y:N2}|{y,8:F1}|")
printfn "%d of %d cases differ" failures total
