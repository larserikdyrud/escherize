namespace Escherize.Templates;

/// <summary>
/// The nine general isohedral templates of SPEC §5.2.
/// </summary>
/// <remarks>
/// The glide axes and the sign of the rotation angles are the two details SPEC §0.4
/// leaves open. They were settled by the validation of SPEC §8.2 and are recorded in
/// DECISIONS.md; the values below are that outcome.
/// </remarks>
public static class TemplateLibrary
{
    /// <summary>
    /// The sign applied to every rotation angle. Decision D2: a counter-clockwise tile has
    /// its interior angle alpha at a rotation centre, and the relation of SPEC §5.1 then
    /// needs theta = -alpha.
    /// </summary>
    internal const double ThetaSign = -1.0;

    /// <summary>
    /// Decision D1, first half: IH2, IH3 and IH5 need every glide pair on the same axis.
    /// Using both axes on the same template leaves the outline degenerate (IH2) or builds
    /// a tile that overlaps its own neighbours (IH3).
    /// </summary>
    internal const GlideAxis Glide = GlideAxis.X;

    /// <summary>
    /// Decision D1, second half: IH6 is the one template whose two glide pairs need
    /// different axes. With both on the same axis its tiles overlap their neighbours.
    /// </summary>
    internal const GlideAxis CrossGlide = GlideAxis.Y;

    private static readonly TemplateSpec[] AllTemplates = BuildAll();

    /// <summary>The nine templates, in the order of SPEC §5.2.</summary>
    /// <returns>The templates.</returns>
    public static IReadOnlyList<TemplateSpec> All => AllTemplates;

    /// <summary>The names of the nine templates, in order.</summary>
    /// <returns>The names.</returns>
    public static IReadOnlyList<string> Names
    {
        get
        {
            var names = new string[AllTemplates.Length];
            for (int i = 0; i < AllTemplates.Length; i++)
            {
                names[i] = AllTemplates[i].Name;
            }

            return names;
        }
    }

    /// <summary>Returns a template by name, such as <c>IH4</c>.</summary>
    /// <param name="name">The template name, case insensitive.</param>
    /// <returns>The template.</returns>
    /// <exception cref="ArgumentException">No template has that name.</exception>
    public static TemplateSpec ByName(string name)
    {
        foreach (TemplateSpec template in AllTemplates)
        {
            if (string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return template;
            }
        }

        throw new ArgumentException($"There is no template named '{name}'.", nameof(name));
    }

    /// <summary>The index of a template in <see cref="All"/>, used as a sort key (SPEC §7.1).</summary>
    /// <param name="name">The template name.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException">No template has that name.</exception>
    public static int IndexOf(string name)
    {
        for (int i = 0; i < AllTemplates.Length; i++)
        {
            if (string.Equals(AllTemplates[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentException($"There is no template named '{name}'.", nameof(name));
    }

    /// <summary>Builds the table of SPEC §5.2.</summary>
    /// <returns>The nine templates.</returns>
    private static TemplateSpec[] BuildAll()
    {
        const int PairA = 0;
        const int PairB = 1;
        const int PairC = 2;

        return
        [
            new TemplateSpec("IH1", "TTTTTT",
            [
                Translation(PairA, 0), Translation(PairB, 1), Translation(PairC, 2),
                Translation(PairA, 0), Translation(PairB, 1), Translation(PairC, 2),
            ]),

            new TemplateSpec("IH2", "TG1G1TG2G2",
            [
                Translation(PairA, 0), Glide2(PairB, 1), Glide2(PairB, 1),
                Translation(PairA, 0), Glide2(PairC, 2), Glide2(PairC, 2),
            ]),

            new TemplateSpec("IH3", "TG1G2TG2G1",
            [
                Translation(PairA, 0), Glide2(PairB, 1), Glide2(PairC, 2),
                Translation(PairA, 0), Glide2(PairC, 2), Glide2(PairB, 1),
            ]),

            new TemplateSpec("IH4", "TCCTCC",
            [
                Translation(PairA, 0), Self(1), Self(2),
                Translation(PairA, 0), Self(3), Self(4),
            ]),

            new TemplateSpec("IH5", "TCCTGG",
            [
                Translation(PairA, 0), Glide2(PairB, 1), Glide2(PairB, 1),
                Translation(PairA, 0), Self(2), Self(3),
            ]),

            // The two glide pairs of IH6 sit on different axes; see decision D1.
            new TemplateSpec("IH6", "CG1CG2G1G2",
            [
                Glide2(PairA, 0, Glide), Glide2(PairB, 1, CrossGlide), Glide2(PairA, 0, Glide),
                Self(2), Glide2(PairB, 1, CrossGlide), Self(3),
            ]),

            new TemplateSpec("IH7", "C3C3C3C3C3C3",
            [
                Rotation(PairA, 0, 120), Rotation(PairA, 0, 120),
                Rotation(PairB, 1, 120), Rotation(PairB, 1, 120),
                Rotation(PairC, 2, 120), Rotation(PairC, 2, 120),
            ]),

            new TemplateSpec("IH21", "CC3C3C6C6",
            [
                Self(0),
                Rotation(PairA, 1, 120), Rotation(PairA, 1, 120),
                Rotation(PairB, 2, 60), Rotation(PairB, 2, 60),
            ]),

            new TemplateSpec("IH28", "CC4C4C4C4",
            [
                Self(0),
                Rotation(PairA, 1, 90), Rotation(PairA, 1, 90),
                Rotation(PairB, 2, 90), Rotation(PairB, 2, 90),
            ]),
        ];
    }

    /// <summary>A C edge.</summary>
    /// <param name="kVar">The k variable.</param>
    /// <returns>The edge.</returns>
    private static EdgeSpec Self(int kVar) => new(EdgeKind.C, -1, kVar);

    /// <summary>One edge of a translation pair.</summary>
    /// <param name="pairId">The pair identifier.</param>
    /// <param name="kVar">The k variable.</param>
    /// <returns>The edge.</returns>
    private static EdgeSpec Translation(int pairId, int kVar) => new(EdgeKind.T, pairId, kVar);

    /// <summary>One edge of a glide pair, using the axis decided in DECISIONS.md.</summary>
    /// <param name="pairId">The pair identifier.</param>
    /// <param name="kVar">The k variable.</param>
    /// <param name="axis">The mirror axis; the default is the one used by most templates.</param>
    /// <returns>The edge.</returns>
    private static EdgeSpec Glide2(int pairId, int kVar, GlideAxis axis = Glide) =>
        new(EdgeKind.G, pairId, kVar, axis);

    /// <summary>One edge of a rotation pair, using the sign decided in DECISIONS.md.</summary>
    /// <param name="pairId">The pair identifier.</param>
    /// <param name="kVar">The k variable.</param>
    /// <param name="degrees">The magnitude of the rotation angle in degrees.</param>
    /// <returns>The edge.</returns>
    private static EdgeSpec Rotation(int pairId, int kVar, double degrees) =>
        new(EdgeKind.R, pairId, kVar, GlideAxis.None, ThetaSign * degrees);
}
