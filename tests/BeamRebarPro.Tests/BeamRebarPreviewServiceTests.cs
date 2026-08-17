using BeamRebarPro.Models;
using BeamRebarPro.Services;
using RevitAPP.Core.Models;
using Xunit;

namespace BeamRebarPro.Tests;

/// <summary>
/// Kiểm tra bản xem trước dựng đúng từ cấu hình người dùng nhập, kể cả khi cấu hình còn dở dang.
/// </summary>
public sealed class BeamRebarPreviewServiceTests
{
    private static QuickSettingModel Model() => new()
    {
        MainTop = new MainBarConfig { Count = 3, Diameter = new RebarDiameter(16) },
        MainBottom = new MainBarConfig { Count = 3, Diameter = new RebarDiameter(20) },
        Stirrup = new StirrupConfig
        {
            Diameter = new RebarDiameter(8),
            Mode = StirrupMode.TwoEnds,
            SpacingEndMm = 150,
            SpacingMidMm = 200
        },
        Cover = new CoverSettings { TopMm = 25, BottomMm = 25, SideMm = 25 }
    };

    private static IReadOnlyList<SpanInfo> OneSpan(double lengthMm = 6000) =>
        [new SpanInfo(0, lengthMm)];

    [Fact]
    public void Build_MainBars_ProducesOneBarPerConfiguredCount()
    {
        var plan = BeamRebarPreviewService.Build(Model(), OneSpan());

        Assert.Equal(3, plan.Paths.Count(p => p.Kind == BeamRebarPathKind.MainTop));
        Assert.Equal(3, plan.Paths.Count(p => p.Kind == BeamRebarPathKind.MainBottom));
    }

    [Fact]
    public void Build_TopBarsSitAboveBottomBars()
    {
        var plan = BeamRebarPreviewService.Build(Model(), OneSpan());

        var top = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainTop).Points[0].Zmm;
        var bottom = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainBottom).Points[0].Zmm;

        Assert.True(top > bottom);
    }

    [Fact]
    public void Build_StirrupsAreClosedLoopsWithFourCorners()
    {
        var plan = BeamRebarPreviewService.Build(Model(), OneSpan());

        var stirrups = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.Stirrup).ToList();

        Assert.NotEmpty(stirrups);
        Assert.All(stirrups, s =>
        {
            Assert.True(s.IsClosedLoop);
            Assert.Equal(4, s.Points.Count);
        });
    }

    [Fact]
    public void Build_TighterEndSpacing_YieldsMoreStirrups()
    {
        var sparse = Model() with { Stirrup = Model().Stirrup with { SpacingEndMm = 300, SpacingMidMm = 300 } };
        var dense = Model() with { Stirrup = Model().Stirrup with { SpacingEndMm = 100, SpacingMidMm = 100 } };

        var sparseCount = BeamRebarPreviewService.Build(sparse, OneSpan()).Stirrups.Count();
        var denseCount = BeamRebarPreviewService.Build(dense, OneSpan()).Stirrups.Count();

        Assert.True(denseCount > sparseCount);
    }

    /// <summary>Nhịp mang hình học thật đọc từ mô hình: tiết diện 250×750, cao độ +3500, nằm xiên.</summary>
    private static SpanInfo RealSpan(double lengthMm = 6000) => new(0, lengthMm)
    {
        SectionWidthMm = 250,
        SectionHeightMm = 750,
        TopElevationMm = 3500,
        StartXMm = 1000,
        StartYMm = 2000,
        EndXMm = 1000 + lengthMm * 0.6,
        EndYMm = 2000 + lengthMm * 0.8
    };

    [Fact]
    public void Build_WithRealBeam_UsesActualSectionWidthNotAssumedOne()
    {
        // Tiết diện phải lấy từ dầm thật; lấy giá trị mặc định sẽ khiến thép xem trước rộng sai.
        var plan = BeamRebarPreviewService.Build(Model(), [RealSpan()]);

        var beam = plan.Context.First(c => c.Kind == BeamRebarContextKind.Beam);
        Assert.Equal(250, beam.WidthMm, 6);
        Assert.Equal(750, beam.HeightMm, 6);
    }

    [Fact]
    public void Build_WithRealBeam_PlacesBarsAtActualElevation()
    {
        var plan = BeamRebarPreviewService.Build(Model(), [RealSpan()]);

        var topBar = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainTop);

        // Thanh trên nằm ngay dưới mặt trên thật (+3500), không phải cao độ giả định.
        Assert.InRange(topBar.Points[0].Zmm, 3400, 3500);
    }

    [Fact]
    public void Build_SkewedBeam_FollowsActualPlanDirection()
    {
        // Dầm xiên trong mặt bằng: thép phải chạy theo trục dầm thật, không nằm dọc trục X.
        var span = RealSpan();
        var plan = BeamRebarPreviewService.Build(Model(), [span]);

        var bar = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainBottom);
        var deltaX = bar.Points[^1].Xmm - bar.Points[0].Xmm;
        var deltaY = bar.Points[^1].Ymm - bar.Points[0].Ymm;

        Assert.True(Math.Abs(deltaY) > Math.Abs(deltaX) * 0.5,
            "Thép phải nghiêng theo tuyến dầm chứ không chạy thẳng theo trục X.");
    }

    [Fact]
    public void Build_SkewedBeam_KeepsStirrupsPerpendicularToBeamAxis()
    {
        var plan = BeamRebarPreviewService.Build(Model(), [RealSpan()]);

        var stirrup = plan.Stirrups.First();
        // Bốn góc đai nằm trong một mặt phẳng cắt ngang: hai góc trên cùng cao độ.
        Assert.Equal(stirrup.Points[0].Zmm, stirrup.Points[1].Zmm, 6);
        Assert.Equal(stirrup.Points[2].Zmm, stirrup.Points[3].Zmm, 6);
    }

    [Fact]
    public void Build_SecondaryBeamOnSkewedSpan_IsProjectedOntoBeamAxis()
    {
        // Điểm dầm phụ nằm ở giữa tuyến dầm xiên; phải chiếu lên trục dầm chứ không lấy riêng trục X.
        var span = RealSpan();
        var midXmm = (span.StartXMm + span.EndXMm) / 2;
        var midYmm = (span.StartYMm + span.EndYMm) / 2;
        var secondary = new[]
        {
            new SecondaryBeamInfo(new Point3(midXmm / 304.8, midYmm / 304.8, 0), 100 / 304.8)
        };

        var plan = BeamRebarPreviewService.Build(Model(), [span], secondary);

        Assert.Contains(plan.Paths, p => p.Kind == BeamRebarPathKind.StirrupSecondary);
        Assert.Contains(plan.Context, c => c.Kind == BeamRebarContextKind.CrossBeam);
    }

    [Fact]
    public void Build_MultipleRealSpans_ReportsTotalRunLength()
    {
        var spans = new[]
        {
            RealSpan(4000) with { Index = 0 },
            RealSpan(5000) with { Index = 1 }
        };

        var plan = BeamRebarPreviewService.Build(Model(), spans);

        Assert.Equal(9000, plan.TotalLengthMm, 3);
        Assert.Equal(3, plan.SupportStationsMm.Count);
    }

    [Fact]
    public void SpanInfo_WithoutGeometry_IsRecognisedAsIncomplete()
    {
        // Nhịp thiếu tiết diện phải bị nhận ra, nếu không bản xem trước sẽ âm thầm vẽ dầm mặc định.
        Assert.False(new SpanInfo(0, 6000).HasRealGeometry);
        Assert.True(RealSpan().HasRealGeometry);
    }

    [Fact]
    public void SpanInfo_CopiedWithNewIndex_KeepsRealGeometry()
    {
        // Bảng nhịp của màn chi tiết sao chép lại nhịp; hình học thật không được rơi rụng khi sao chép.
        var copy = RealSpan() with { Index = 2 };

        Assert.True(copy.HasRealGeometry);
        Assert.Equal(250, copy.SectionWidthMm);
        Assert.Equal(3500, copy.TopElevationMm);
    }

    [Fact]
    public void Build_FallbackSpan_DoesNotClaimRealGeometry()
    {
        var plan = BeamRebarPreviewService.Build(Model(), []);
        var beam = plan.Context.First(c => c.Kind == BeamRebarContextKind.Beam);

        // Khi chưa chọn dầm, bản xem trước dùng dầm mẫu — kích thước này không được nhầm là số thật.
        Assert.Equal(300, beam.WidthMm, 6);
        Assert.Equal(600, beam.HeightMm, 6);
    }

    [Fact]
    public void Build_NoBeamSelected_StillShowsRepresentativeBeam()
    {
        // Người dùng mở hộp thoại trước khi chọn dầm — khung xem trước vẫn phải có nội dung.
        var plan = BeamRebarPreviewService.Build(Model(), []);

        Assert.False(plan.IsEmpty);
        Assert.Contains(plan.Context, c => c.Kind == BeamRebarContextKind.Beam);
    }

    [Fact]
    public void Build_IncludesConcreteContextForBeamAndSupports()
    {
        var plan = BeamRebarPreviewService.Build(Model(), OneSpan());

        Assert.Contains(plan.Context, c => c.Kind == BeamRebarContextKind.Beam);
        Assert.Contains(plan.Context, c => c.Kind == BeamRebarContextKind.Column);
    }

    [Fact]
    public void Build_MultipleSpans_MainBarsRunContinuouslyThroughInnerSupports()
    {
        // Thanh chủ chạy suốt cả dầm vật lý, không cắt tại gối giữa — nếu cắt, bản xem trước sẽ hiện
        // các đoạn đứt trong khi mô hình thật có thanh liền.
        var spans = new[] { new SpanInfo(0, 4000), new SpanInfo(1, 5000) };

        var plan = BeamRebarPreviewService.Build(Model(), spans);
        var topBars = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.MainTop).ToList();

        Assert.Equal(3, topBars.Count); // 3 thanh chủ, mỗi thanh một sợi liền.
        Assert.All(topBars, bar =>
        {
            var length = bar.Points[^1].Xmm - bar.Points[0].Xmm;
            Assert.True(length > 8000, $"Thanh chủ chỉ dài {length:F0}mm, đã bị cắt tại gối giữa.");
        });
    }

    [Fact]
    public void Build_MultipleSpans_AdditionalBarsStayWithinTheirOwnSpan()
    {
        // Ngược với thép chủ: thép gia cường dưới thuộc về từng nhịp riêng.
        var model = Model() with
        {
            BottomAdditional = new AdditionalBarConfig
            {
                Enabled = true, Count = 2, Diameter = new RebarDiameter(16),
                Side = AdditionalBarSide.BottomAtMidspan
            }
        };
        var spans = new[] { new SpanInfo(0, 4000), new SpanInfo(1, 5000) };

        var plan = BeamRebarPreviewService.Build(model, spans);
        var bars = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalBottom).ToList();

        Assert.NotEmpty(bars);
        Assert.All(bars, bar =>
        {
            var length = bar.Points[^1].Xmm - bar.Points[0].Xmm;
            Assert.True(length < 5000, $"Thép gia cường dài {length:F0}mm, đã tràn qua nhịp khác.");
        });
    }

    [Fact]
    public void Build_MultipleSpans_StirrupsCoverEverySpan()
    {
        var spans = new[] { new SpanInfo(0, 4000), new SpanInfo(1, 5000) };

        var plan = BeamRebarPreviewService.Build(Model(), spans);

        // Mỗi nhịp phải có đai của riêng nó; thiếu nhịp nào là mô hình thật có đai mà xem trước không thấy.
        Assert.Contains(plan.Stirrups, s => s.SpanIndex == 0);
        Assert.Contains(plan.Stirrups, s => s.SpanIndex == 1);
    }

    [Fact]
    public void Build_MultipleSpans_ChainsThemIntoOneRun()
    {
        var spans = new[] { new SpanInfo(0, 4000), new SpanInfo(1, 5000) };

        var plan = BeamRebarPreviewService.Build(Model(), spans);

        Assert.Equal(9000, plan.TotalLengthMm, 6);
        Assert.Equal(3, plan.SupportStationsMm.Count);
    }

    private static QuickSettingModel ModelWithTopAdditional() => Model() with
    {
        TopAdditional = new AdditionalBarConfig
        {
            Enabled = true, Count = 2, Diameter = new RebarDiameter(16),
            LengthPercent = 50, EdgeHookDownLengthMm = 300,
            Side = AdditionalBarSide.TopAtSupport
        }
    };

    [Fact]
    public void Build_TopAdditionalAtInnerColumn_IsOneContinuousBarThroughTheSupport()
    {
        // Tại cột giữa, thép gia cường trên là MỘT cây chạy xuyên qua gối — không phải hai cây cắt rời.
        var spans = new[] { new SpanInfo(0, 6000), new SpanInfo(1, 6000) };

        var plan = BeamRebarPreviewService.Build(ModelWithTopAdditional(), spans);
        var bars = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalTop).ToList();

        // Gối giữa nằm tại 6000mm: phải có thanh bắc qua nó.
        var throughInner = bars.Where(b => b.Points[0].Xmm < 6000 && b.Points[^1].Xmm > 6000).ToList();

        Assert.NotEmpty(throughInner);
        Assert.Equal(2, throughInner.Count); // đúng số cây đã cấu hình.
    }

    [Fact]
    public void Build_TopAdditionalAtInnerColumn_HasNoHookBend()
    {
        // Thanh xuyên cột giữa chạy thẳng; bẻ móc chỉ có ở hai đầu tuyến dầm.
        var spans = new[] { new SpanInfo(0, 6000), new SpanInfo(1, 6000) };

        var plan = BeamRebarPreviewService.Build(ModelWithTopAdditional(), spans);
        var throughInner = plan.Paths
            .Where(p => p.Kind == BeamRebarPathKind.AdditionalTop)
            .Where(p => p.Points[0].Xmm < 6000 && p.Points[^1].Xmm > 6000);

        Assert.All(throughInner, bar =>
            Assert.Equal(2, bar.Points.Count)); // hai điểm = thanh thẳng, không có đỉnh bẻ.
    }

    [Fact]
    public void Build_TopAdditionalAtEndSupports_BendsDownAtBeamEnds()
    {
        var spans = new[] { new SpanInfo(0, 6000), new SpanInfo(1, 6000) };

        var plan = BeamRebarPreviewService.Build(ModelWithTopAdditional(), spans);
        var bars = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalTop).ToList();

        // Gối biên trái tại 0: thanh ở đó phải có đỉnh bẻ xuống.
        var atStart = bars.Where(b => b.Points[0].Xmm < 100).ToList();
        Assert.NotEmpty(atStart);
        Assert.All(atStart, bar => Assert.True(bar.Points.Count > 2, "Thanh ở gối biên phải có móc bẻ xuống."));
    }

    [Fact]
    public void Build_TopAdditionalLength_FollowsExplicitSideLengthNotDefault()
    {
        // Người dùng nhập chiều dài riêng cho mỗi bên: thanh phải dài đúng như vậy, không lấy 1/4 nhịp.
        var model = Model() with
        {
            TopAdditional = new AdditionalBarConfig
            {
                Enabled = true, Count = 1, Diameter = new RebarDiameter(16),
                LeftLengthMm = 800, RightLengthMm = 800,
                Side = AdditionalBarSide.TopAtSupport
            }
        };
        var spans = new[] { new SpanInfo(0, 6000), new SpanInfo(1, 6000) };

        var plan = BeamRebarPreviewService.Build(model, spans);
        var throughInner = plan.Paths
            .First(p => p.Kind == BeamRebarPathKind.AdditionalTop
                        && p.Points[0].Xmm < 6000 && p.Points[^1].Xmm > 6000);

        // 800 mỗi bên cộng hai nửa bề rộng gối (mặc định 200) = 2000mm.
        var length = throughInner.Points[^1].Xmm - throughInner.Points[0].Xmm;
        Assert.Equal(2000, length, 1);
    }

    [Fact]
    public void Build_TopAdditionalLength_FollowsRatioOfSpan()
    {
        var model = Model() with
        {
            TopAdditional = new AdditionalBarConfig
            {
                Enabled = true, Count = 1, Diameter = new RebarDiameter(16),
                LeftRatio = 0.2, RightRatio = 0.2,
                Side = AdditionalBarSide.TopAtSupport
            }
        };
        var spans = new[] { new SpanInfo(0, 5000), new SpanInfo(1, 5000) };

        var plan = BeamRebarPreviewService.Build(model, spans);
        var throughInner = plan.Paths
            .First(p => p.Kind == BeamRebarPathKind.AdditionalTop
                        && p.Points[0].Xmm < 5000 && p.Points[^1].Xmm > 5000);

        // 0.2 × 5000 = 1000mm mỗi bên, cộng hai nửa bề rộng gối 200 = 2400mm.
        var length = throughInner.Points[^1].Xmm - throughInner.Points[0].Xmm;
        Assert.Equal(2400, length, 1);
    }

    [Fact]
    public void Build_ClosedAdditionalStirrup_IsDrawnAlongTheSpan()
    {
        var plan = BeamRebarPreviewService.Build(
            ModelWithAdditionalStirrup(AdditionalStirrupType.Closed), [new SpanInfo(0, 6000)]);

        var extra = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalStirrupClosed).ToList();

        Assert.NotEmpty(extra);
        Assert.All(extra, s =>
        {
            Assert.True(s.IsClosedLoop);
            Assert.Equal(4, s.Points.Count);
            Assert.Equal(8, s.DiameterMm);
        });
    }

    [Fact]
    public void Build_ClosedAdditionalStirrup_IsNarrowerThanMainStirrup()
    {
        // Đai phụ chỉ ôm vài thanh giữa nên hẹp hơn đai chính bao cả tiết diện.
        var plan = BeamRebarPreviewService.Build(
            ModelWithAdditionalStirrup(AdditionalStirrupType.Closed), [new SpanInfo(0, 6000)]);

        var extraWidth = Width(plan.Paths.First(p => p.Kind == BeamRebarPathKind.AdditionalStirrupClosed));
        var mainWidth = Width(plan.Paths.First(p => p.Kind == BeamRebarPathKind.Stirrup));

        Assert.True(extraWidth > 0);
        Assert.True(extraWidth < mainWidth);
    }

    [Fact]
    public void Build_CHookAdditionalStirrup_IsSingleVerticalBar()
    {
        var plan = BeamRebarPreviewService.Build(
            ModelWithAdditionalStirrup(AdditionalStirrupType.CHook), [new SpanInfo(0, 6000)]);

        var hooks = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalStirrupCHook).ToList();

        Assert.NotEmpty(hooks);
        Assert.All(hooks, h =>
        {
            Assert.False(h.IsClosedLoop);
            Assert.Equal(2, h.Points.Count);
        });
    }

    [Fact]
    public void Build_AdditionalStirrup_NeedsAtLeastThreeMainBars()
    {
        // Dưới ba thanh chủ thì không có thanh giữa nào để ôm.
        var model = ModelWithAdditionalStirrup(AdditionalStirrupType.Closed) with
        {
            MainTop = new MainBarConfig { Count = 2, Diameter = new RebarDiameter(16) }
        };

        var plan = BeamRebarPreviewService.Build(model, [new SpanInfo(0, 6000)]);

        Assert.DoesNotContain(plan.Paths, IsAdditionalStirrup);
    }

    [Fact]
    public void Build_DisabledAdditionalStirrup_IsNotDrawn()
    {
        var model = Model() with
        {
            MainTop = new MainBarConfig { Count = 4, Diameter = new RebarDiameter(16) },
            Stirrup = Model().Stirrup with
            {
                AdditionalStirrups = [new AdditionalStirrupConfig { Enabled = false }]
            }
        };

        var plan = BeamRebarPreviewService.Build(model, [new SpanInfo(0, 6000)]);

        Assert.DoesNotContain(plan.Paths, IsAdditionalStirrup);
    }

    [Fact]
    public void Build_AdditionalStirrup_MatchesMainStirrupCount()
    {
        // Rải cùng vùng, cùng bước với đai chính.
        var plan = BeamRebarPreviewService.Build(
            ModelWithAdditionalStirrup(AdditionalStirrupType.Closed), [new SpanInfo(0, 6000)]);

        Assert.Equal(
            plan.Paths.Count(p => p.Kind == BeamRebarPathKind.Stirrup),
            plan.Paths.Count(p => p.Kind == BeamRebarPathKind.AdditionalStirrupClosed));
    }

    private static QuickSettingModel ModelWithAdditionalStirrup(AdditionalStirrupType type) => Model() with
    {
        MainTop = new MainBarConfig { Count = 4, Diameter = new RebarDiameter(16) },
        Stirrup = Model().Stirrup with
        {
            AdditionalStirrups =
            [
                new AdditionalStirrupConfig
                {
                    Enabled = true, Diameter = new RebarDiameter(8),
                    Type = type, StartBar = 2, EndBar = 3
                }
            ]
        }
    };

    private static bool IsAdditionalStirrup(BeamRebarPath path) => path.Kind
        is BeamRebarPathKind.AdditionalStirrupClosed or BeamRebarPathKind.AdditionalStirrupCHook;

    private static double Width(BeamRebarPath path) => Math.Abs(path.Points[1].Ymm - path.Points[0].Ymm);

    [Fact]
    public void Build_MainTopAnchor_BendsBarDownAtBothEnds()
    {
        // Ô "Anchor Left/Right" ở màn chi tiết là đoạn bẻ đầu thép chủ; bỏ qua thì thanh vẽ ra thẳng
        // trong khi mô hình thật có móc.
        var model = Model() with
        {
            MainTop = new MainBarConfig
            {
                Count = 3, Diameter = new RebarDiameter(16),
                AnchorLeftMm = 200, AnchorRightMm = 200
            }
        };

        var plan = BeamRebarPreviewService.Build(model, [new SpanInfo(0, 6000)]);
        var bar = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainTop);

        Assert.Equal(4, bar.Points.Count); // hai đỉnh bẻ cộng hai đầu thanh.
        Assert.Equal(bar.Points[1].Zmm - 200, bar.Points[0].Zmm, 6);
        Assert.Equal(bar.Points[^2].Zmm - 200, bar.Points[^1].Zmm, 6);
    }

    [Fact]
    public void Build_MainTopAnchor_WinsOverGeneralBendLength()
    {
        var model = Model() with
        {
            MainTop = new MainBarConfig
            {
                Count = 1, Diameter = new RebarDiameter(16),
                AnchorLeftMm = 350, TopEndBendDownLengthMm = 100
            }
        };

        var plan = BeamRebarPreviewService.Build(model, [new SpanInfo(0, 6000)]);
        var bar = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainTop);

        Assert.Equal(bar.Points[1].Zmm - 350, bar.Points[0].Zmm, 6);
    }

    [Fact]
    public void Build_MainBottomAnchor_BendsBarUpwards()
    {
        // Thép dưới quặp ngược lên, khác chiều với thép trên.
        var model = Model() with
        {
            MainBottom = new MainBarConfig
            {
                Count = 3, Diameter = new RebarDiameter(20),
                AnchorLeftMm = 250, AnchorRightMm = 250
            }
        };

        var plan = BeamRebarPreviewService.Build(model, [new SpanInfo(0, 6000)]);
        var bar = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainBottom);

        Assert.Equal(4, bar.Points.Count);
        Assert.Equal(bar.Points[1].Zmm + 250, bar.Points[0].Zmm, 6);
    }

    [Fact]
    public void Build_MainBarWithoutAnchor_StaysStraight()
    {
        var plan = BeamRebarPreviewService.Build(Model(), [new SpanInfo(0, 6000)]);

        Assert.All(plan.Paths.Where(p => p.Kind is BeamRebarPathKind.MainTop or BeamRebarPathKind.MainBottom),
            bar => Assert.Equal(2, bar.Points.Count));
    }

    [Fact]
    public void Build_DetailTopItems_TakePrecedenceOverCombinedConfig()
    {
        // Màn chi tiết quản lý từng cây theo từng gối. Bỏ qua danh sách này thì mọi thao tác thêm,
        // xoá, sửa trong màn chi tiết đều không hiện ra ở bản xem trước.
        var model = Model() with
        {
            TopAdditional = new AdditionalBarConfig
            {
                Enabled = true, Count = 9, Diameter = new RebarDiameter(25),
                Side = AdditionalBarSide.TopAtSupport
            },
            TopAdditionalItems =
            [
                new AdditionalBarConfig
                {
                    Enabled = true, Count = 2, Diameter = new RebarDiameter(16),
                    StartPointIndex = 1, EndPointIndex = 1,
                    Side = AdditionalBarSide.TopAtSupport
                }
            ]
        };
        var spans = new[] { new SpanInfo(0, 6000), new SpanInfo(1, 6000) };

        var plan = BeamRebarPreviewService.Build(model, spans);
        var bars = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalTop).ToList();

        // Chỉ cây của gối 1: 2 thanh D16, không phải 9 thanh D25 của cấu hình gộp.
        Assert.Equal(2, bars.Count);
        Assert.All(bars, b => Assert.Equal(16, b.DiameterMm));
    }

    [Fact]
    public void Build_DetailTopItems_PlaceBarsAtTheirOwnSupport()
    {
        var model = Model() with
        {
            TopAdditionalItems =
            [
                new AdditionalBarConfig
                {
                    Enabled = true, Count = 1, Diameter = new RebarDiameter(16),
                    StartPointIndex = 1, EndPointIndex = 1,
                    Side = AdditionalBarSide.TopAtSupport
                }
            ]
        };
        var spans = new[] { new SpanInfo(0, 6000), new SpanInfo(1, 6000) };

        var plan = BeamRebarPreviewService.Build(model, spans);
        var bar = Assert.Single(plan.Paths, p => p.Kind == BeamRebarPathKind.AdditionalTop);

        // Gối 1 nằm tại 6000mm: thanh phải bắc qua đúng vị trí đó.
        Assert.True(bar.Points[0].Xmm < 6000 && bar.Points[^1].Xmm > 6000);
    }

    [Fact]
    public void Build_DetailTopItems_RemovedItemDisappearsFromPreview()
    {
        // Người dùng xoá hết cây trong màn chi tiết nhưng cấu hình gộp vẫn bật: bản xem trước phải
        // trống, nếu không thì cây đã xoá vẫn hiện lại.
        var model = Model() with
        {
            TopAdditional = new AdditionalBarConfig
            {
                Enabled = true, Count = 2, Diameter = new RebarDiameter(16),
                Side = AdditionalBarSide.TopAtSupport
            },
            TopAdditionalItems =
            [
                new AdditionalBarConfig { Enabled = false, Count = 2, StartPointIndex = 0, EndPointIndex = 0 }
            ]
        };

        var plan = BeamRebarPreviewService.Build(model, [new SpanInfo(0, 6000)]);

        Assert.DoesNotContain(plan.Paths, p => p.Kind == BeamRebarPathKind.AdditionalTop);
    }

    [Fact]
    public void Build_DetailBottomItems_StayWithinTheirDeclaredSpan()
    {
        var model = Model() with
        {
            BottomAdditionalItems =
            [
                new AdditionalBarConfig
                {
                    Enabled = true, Count = 3, Diameter = new RebarDiameter(20),
                    StartPointIndex = 1, EndPointIndex = 2,
                    Side = AdditionalBarSide.BottomAtMidspan
                }
            ]
        };
        var spans = new[] { new SpanInfo(0, 6000), new SpanInfo(1, 6000) };

        var plan = BeamRebarPreviewService.Build(model, spans);
        var bars = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalBottom).ToList();

        // Chỉ nhịp 1 có thép; nhịp 0 không khai báo nên phải trống.
        Assert.Equal(3, bars.Count);
        Assert.All(bars, b => Assert.Equal(1, b.SpanIndex));
    }

    [Fact]
    public void Build_TopAdditionalBars_AppearAtBothSupports()
    {
        var model = Model() with
        {
            TopAdditional = new AdditionalBarConfig
            {
                Enabled = true, Count = 2, Diameter = new RebarDiameter(16),
                LengthPercent = 50, Side = AdditionalBarSide.TopAtSupport
            }
        };

        var plan = BeamRebarPreviewService.Build(model, OneSpan());
        var additional = plan.Paths.Where(p => p.Kind == BeamRebarPathKind.AdditionalTop).ToList();

        Assert.Equal(4, additional.Count); // 2 cây × 2 đầu gối.
        Assert.Contains(additional, p => p.Points[0].Xmm < 3000);
        Assert.Contains(additional, p => p.Points[^1].Xmm > 3000);
    }

    [Fact]
    public void Build_BottomAdditionalBars_SitAroundMidspan()
    {
        var model = Model() with
        {
            BottomAdditional = new AdditionalBarConfig
            {
                Enabled = true, Count = 2, Diameter = new RebarDiameter(16),
                Side = AdditionalBarSide.BottomAtMidspan
            }
        };

        var plan = BeamRebarPreviewService.Build(model, OneSpan());
        var bar = plan.Paths.First(p => p.Kind == BeamRebarPathKind.AdditionalBottom);

        Assert.True(bar.Points[0].Xmm > 0);
        Assert.True(bar.Points[^1].Xmm < 6000);
    }

    [Fact]
    public void Build_DisabledAdditionalBars_AreNotDrawn()
    {
        var plan = BeamRebarPreviewService.Build(Model(), OneSpan());

        Assert.DoesNotContain(plan.Paths, p =>
            p.Kind is BeamRebarPathKind.AdditionalTop or BeamRebarPathKind.AdditionalBottom);
    }

    [Fact]
    public void Build_VerySmallSpacing_StillDrawsEveryStirrup()
    {
        // Bước 1mm trên nhịp 6m là hơn 6000 đai — vẫn trong ngân sách, và bản xem trước phải vẽ đủ
        // thay vì lược bớt, vì số đai hiển thị chính là số đai sẽ được tạo.
        var model = Model() with { Stirrup = Model().Stirrup with { SpacingEndMm = 1, SpacingMidMm = 1 } };

        var plan = BeamRebarPreviewService.Build(model, OneSpan());

        // 6000 khoảng 1mm, cộng đai đầu của mỗi vùng: ba vùng đai đều có thanh ở cả hai biên vùng,
        // nên hai ranh giới vùng mang hai đai trùng vị trí — đúng như cách Revit rải từng vùng riêng.
        Assert.Equal(6003, plan.Stirrups.Count());
    }

    [Fact]
    public void Build_SpacingBeyondBudget_YieldsEmptyPlanInsteadOfHanging()
    {
        // Nhịp dài với bước rất nhỏ vượt trần dựng hình: bỏ trống khung thay vì treo hộp thoại.
        var model = Model() with { Stirrup = Model().Stirrup with { SpacingEndMm = 1, SpacingMidMm = 1 } };

        var plan = BeamRebarPreviewService.Build(model, OneSpan(lengthMm: 40_000));

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Build_ZeroBarCount_ProducesNoLongitudinalBars()
    {
        var model = Model() with
        {
            MainTop = new MainBarConfig { Count = 0, Diameter = new RebarDiameter(16) },
            MainBottom = new MainBarConfig { Count = 0, Diameter = new RebarDiameter(16) }
        };

        var plan = BeamRebarPreviewService.Build(model, OneSpan());

        Assert.Empty(plan.Longitudinal);
    }

    [Fact]
    public void Build_BendDownConfigured_AddsHookVertexToTopBars()
    {
        var model = Model() with
        {
            MainTop = new MainBarConfig
            {
                Count = 2, Diameter = new RebarDiameter(16), TopEndBendDownLengthMm = 300
            }
        };

        var plan = BeamRebarPreviewService.Build(model, OneSpan());
        var bar = plan.Paths.First(p => p.Kind == BeamRebarPathKind.MainTop);

        Assert.Equal(4, bar.Points.Count); // hai đỉnh bẻ cộng hai đầu thanh.
        Assert.True(bar.Points[0].Zmm < bar.Points[1].Zmm);
    }

    [Fact]
    public void Build_SecondaryBeam_CreatesReinforcingStirrupCluster()
    {
        // Dầm phụ tại 3000mm: toạ độ Revit tính bằng feet.
        var secondary = new[] { new SecondaryBeamInfo(new Point3(3000 / 304.8, 0, 0), 100 / 304.8) };

        var plan = BeamRebarPreviewService.Build(Model(), OneSpan(), secondary);

        Assert.Contains(plan.Paths, p => p.Kind == BeamRebarPathKind.StirrupSecondary);
    }
}
