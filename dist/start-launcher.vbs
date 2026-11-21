' Launch LolLauncher.exe without going through Explorer's SmartScreen check.
' WScript.Shell.Run creates the process directly (CreateProcess).
Dim fso, sh, base, args, a
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)

args = ""
For Each a In WScript.Arguments
    args = args & " """ & a & """"
Next

Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = base
sh.Run """" & base & "\LolLauncher.exe""" & args, 1, False
