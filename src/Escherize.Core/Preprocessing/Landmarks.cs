using Escherize.Geometry;

namespace Escherize.Preprocessing;

/// <summary>
/// A place on the outline that matters more than the rest, so the search is pushed to fit
/// it closely (SPEC §4.6).
/// </summary>
/// <param name="Name">A label, for the report.</param>
/// <param name="Position">Where it is, in the coordinates of the input file.</param>
/// <param name="Weight">How much more it counts, at least one.</param>
/// <param name="Sigma">How far its influence reaches, as a fraction of the perimeter.</param>
public sealed record Landmark(string Name, Vec2 Position, double Weight, double Sigma = Landmarks.DefaultSigma);

/// <summary>Turns landmarks into a weight per goal point (SPEC §4.6).</summary>
public static class Landmarks
{
    /// <summary>The default width of a landmark, as a fraction of the perimeter (SPEC §4.6).</summary>
    public const double DefaultSigma = 0.03;

    /// <summary>
    /// The weight of every goal point: one, plus a gaussian bump around each landmark
    /// (SPEC §4.6).
    /// </summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="landmarks">The landmarks, in input coordinates.</param>
    /// <returns>One weight per goal point, each at least one.</returns>
    public static double[] Weights(GoalShape goal, IReadOnlyList<Landmark> landmarks)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(landmarks);

        int n = goal.PointCount;
        var weights = new double[n];
        Array.Fill(weights, 1.0);

        foreach (Landmark landmark in landmarks)
        {
            double position = ArcLengthOf(goal, landmark.Position);
            double sigma = landmark.Sigma > 0 ? landmark.Sigma : DefaultSigma;
            double amplitude = Math.Max(0, landmark.Weight - 1);
            if (amplitude <= 0)
            {
                continue;
            }

            for (int t = 0; t < n; t++)
            {
                // The goal is resampled at equal arc length, so point t sits at t / n.
                double distance = CyclicDistance((double)t / n, position);
                weights[t] += amplitude * Math.Exp(-(distance * distance) / (2 * sigma * sigma));
            }
        }

        return weights;
    }

    /// <summary>
    /// The arc length parameter of the point on the outline closest to a position, as a
    /// fraction of the perimeter (SPEC §4.6).
    /// </summary>
    /// <param name="goal">The normalised goal.</param>
    /// <param name="position">The position, in input coordinates.</param>
    /// <returns>The parameter, between zero and one.</returns>
    public static double ArcLengthOf(GoalShape goal, Vec2 position)
    {
        ArgumentNullException.ThrowIfNull(goal);

        // The goal is normalised, so the landmark is brought into the same frame.
        var normalized = new Vec2(
            (position.X - goal.Centroid.X) / goal.Scale,
            (position.Y - goal.Centroid.Y) / goal.Scale);

        ReadOnlySpan<Vec2> points = goal.Points;
        int n = points.Length;

        double best = double.PositiveInfinity;
        double bestParameter = 0;

        for (int t = 0; t < n; t++)
        {
            Vec2 a = points[t];
            Vec2 b = points[(t + 1) % n];
            Vec2 edge = b - a;

            double lengthSquared = edge.LengthSquared;
            double along = lengthSquared > 0
                ? Math.Clamp((normalized - a).Dot(edge) / lengthSquared, 0, 1)
                : 0;

            double distance = normalized.DistanceTo(a + (edge * along));
            if (distance < best)
            {
                best = distance;
                bestParameter = (t + along) / n;
            }
        }

        return bestParameter - Math.Floor(bestParameter);
    }

    /// <summary>The distance between two positions on a closed loop of unit length.</summary>
    /// <param name="first">The first position.</param>
    /// <param name="second">The second position.</param>
    /// <returns>The distance, at most one half.</returns>
    public static double CyclicDistance(double first, double second)
    {
        double difference = Math.Abs(first - second);
        return Math.Min(difference, 1 - difference);
    }
}
