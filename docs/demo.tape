# VHS tape for the README demo (https://github.com/charmbracelet/vhs).
# Regenerate with:  vhs docs/demo.tape   (run from the repository root)

Output docs/demo.gif

Set Shell zsh
Set FontSize 13
Set Width 1200
Set Height 840
Set Padding 12
Set TypingSpeed 40ms

Type "dotnet run --project src/GossNet.Observatory -- --nodes 9 --topology krandom"
Enter

# Build check + cluster startup.
Sleep 5s

# Inject a couple of messages and let the propagation trees draw.
Space
Sleep 2s
Space
Sleep 2.5s

# Walk the selection over and kill a node...
Right
Right
Sleep 500ms
Type "k"
Sleep 1s

# ...then inject and watch the cluster route around the corpse.
Space
Sleep 3s

# Revive it.
Type "r"
Sleep 1.5s

# Continuous injection for a while: the grid pulses, the histogram fills.
Type "a"
Sleep 7s
Type "a"
Sleep 1.5s

Type "q"
Sleep 1s
