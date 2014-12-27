#I @"packages/FAKE/tools/"
#r @"FakeLib.dll"

open Fake


Target "Restore" (fun () ->
    RestorePackages()
)

Target "Clean" (fun () ->
    CleanDir "build"
)


Target "Compile" (fun () ->
    MSBuildRelease "build/Release" "Build" ["Matrjoshka.sln"] |> ignore
)


Target "Default" (fun () -> ())
Target "Rebuild" (fun () -> ())




"Restore" ==> 
    "Compile" ==>
    "Default"


"Clean" ==>
    "Restore"
    "Compile" ==>
    "Rebuild"


// start build
RunTargetOrDefault "Default"

