using BrutalShore.Runtime;
using Shorebound;

// The entry point does nothing but turn a command line into a game, so that everything with a
// decision in it is in a type that can be read, changed and eventually tested on its own.
var options = Options.Parse(args);

// Order matters: the game holds pipelines and buffers created on the host's device, so it must be
// disposed first. Declaring the host first and the game second gets that right, because `using`
// disposes in reverse order of declaration.
using var host = new GameHost(options.Host);
using var game = new ShoreboundGame(options);

host.Run(game);
