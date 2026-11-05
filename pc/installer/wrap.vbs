' HcMic driver wrapper: runs VBCABLE_Setup_x64.exe fully hidden
Set sh = CreateObject("WScript.Shell")
If WScript.Arguments.Count < 1 Then WScript.Quit 1
exe = WScript.Arguments(0)
args = ""
For i = 1 To WScript.Arguments.Count - 1
  args = args & " """ & WScript.Arguments(i) & """"
Next
sh.Run """" & exe & """" & args, 0, True
