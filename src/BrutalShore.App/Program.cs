using BrutalShore.App;

// The entry point does nothing but turn a command line into a game, so that everything with a
// decision in it is in a type that can be read, changed and eventually tested on its own.
var options = GameOptions.Parse(args);

using var game = new Game(options);
game.Run();
