from nosai.mcp.learning import LearningFactory, LearningStatus


def test_learning_candidate_requires_observed_evidence_before_promotion(tmp_path):
    factory = LearningFactory(tmp_path)
    candidate = factory.record_candidate(
        topic="combat/rotation",
        payload={"sequence": ["skill_a", "skill_b"]},
        source="online_session",
        evidence_count=0,
    )
    result = factory.validate_candidate(candidate.candidate_id)
    assert result.status is LearningStatus.REJECTED
    assert "evidence" in result.reason


def test_validated_candidate_is_exported_as_offline_skill(tmp_path):
    factory = LearningFactory(tmp_path)
    candidate = factory.record_candidate(
        topic="combat/rotation",
        payload={"sequence": ["skill_a", "skill_b"]},
        source="online_session",
        evidence_count=5,
    )
    result = factory.validate_candidate(candidate.candidate_id)
    assert result.status is LearningStatus.VALIDATED
    skill = factory.export_offline_skill(candidate.candidate_id)
    assert skill["topic"] == "combat/rotation"
    assert skill["payload"]["sequence"] == ["skill_a", "skill_b"]


