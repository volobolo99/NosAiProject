from nosai.perception import ROI, ROIVision, Frame, BoundingBox, CentroidTracker, PerceptionSnapshot, TrackedEntity, GameStateEvaluator

def test_roi_is_clipped_to_frame():
    boxes = ROIVision([ROI(-5, -5, 200, 200)]).crop_boxes(Frame(100, 80, 1.0, b""))
    assert boxes[0] == ROI(0, 0, 100, 80)

def test_tracker_estimates_velocity():
    tracker = CentroidTracker()
    tracker.update([BoundingBox(0, 0, 10, 10, 1.0, "monster")], 1.0)
    tracks = tracker.update([BoundingBox(10, 0, 10, 10, 1.0, "monster")], 2.0)
    assert tracks[0].vx == 10

def test_game_state_evaluator_fuses_semantics():
    snapshot = PerceptionSnapshot(2.0, 90, 80, 50, (TrackedEntity("monster", 1, 2, 0, 0, .9),), ())
    state = GameStateEvaluator().evaluate(snapshot)
    assert state.enemy_count == 1
    assert state.player_hp_pct == 90
