' "Today" widget launcher - starts the widget with no console window.
' Kept ASCII-only so it is safe under any codepage; the folder path is read at
' runtime, so a Korean folder name is handled correctly.
Option Explicit
Dim sh, fso, base, cmd
Set sh  = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Sta -WindowStyle Hidden -File """ & base & "\launch.ps1"""
sh.Run cmd, 0, False
