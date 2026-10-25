namespace RustSpace.World.Tests;

public class WorldCoordinatesTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(31, 0, 31)]
    [InlineData(32, 1, 0)]
    [InlineData(33, 1, 1)]
    // The negative side is the whole reason this is a shift and a mask. Integer division would put
    // block -1 in chunk 0 at offset -1, which is a coordinate no chunk has.
    [InlineData(-1, -1, 31)]
    [InlineData(-32, -1, 0)]
    [InlineData(-33, -2, 31)]
    public void a_block_coordinate_splits_into_the_chunk_that_holds_it_and_its_offset(
        int block,
        int expectedChunk,
        int expectedLocal
    )
    {
        Assert.Equal(expectedChunk, WorldCoordinates.ToChunk(block));
        Assert.Equal(expectedLocal, WorldCoordinates.ToLocal(block));
    }

    [Fact]
    public void the_split_reassembles_into_the_coordinate_it_came_from()
    {
        for (var block = -200; block <= 200; block++)
        {
            var reassembled = WorldCoordinates.ToChunk(block) * Chunk.Size + WorldCoordinates.ToLocal(block);

            Assert.Equal(block, reassembled);
        }
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.9, 0)]
    [InlineData(1.0, 1)]
    // A position just below the origin is in block -1, not block 0: the block a position is in is
    // the one whose cube contains it, on both sides of zero.
    [InlineData(-0.1, -1)]
    [InlineData(-1.0, -1)]
    [InlineData(-1.1, -2)]
    public void a_position_belongs_to_the_block_whose_cube_contains_it(double position, int expected)
    {
        Assert.Equal(expected, WorldCoordinates.ToBlock(new double3(position, position, position)).X);
    }
}
