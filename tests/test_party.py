from nosai.party import (
    PartnerEntity, PartnerSkill, SpecialistPartnerCard,
    RelationshipTier, SkillRank, PartnerBehavior, PetEntity, PetBehavior,
)


def test_partner_affinity_and_relationship_tier():
    partner = PartnerEntity("p1", "Kliff", trust=75, affection=60, alpha_trust=0.6)
    assert partner.affinity() == 69.0
    assert partner.relationship_tier() is RelationshipTier.TRUSTED


def test_partner_tactical_thresholds():
    partner = PartnerEntity("p1", "Kliff", morale=80, trust=75)
    assert partner.tactical_behavior() is PartnerBehavior.TEAM_SUPPORT
    partner.morale = 30
    partner.trust = 30
    assert partner.tactical_behavior() is PartnerBehavior.HESITATE_OR_RETREAT


def test_partner_sp_skill_cooldown():
    skill = PartnerSkill("s1", "Taunt", SkillRank.A, cooldown=15)
    card = SpecialistPartnerCard("sp1", "Aegir", "WATER", equipped=True, skills=[skill])
    partner = PartnerEntity("p1", "Kliff", active_sp=card)
    skill.remaining_cooldown = 5
    partner.tick(2)
    assert skill.remaining_cooldown == 3
    assert not skill.ready


def test_partner_memory_consolidation_and_decay():
    partner = PartnerEntity("p1", "Kliff")
    partner.register_memory(35, "SAVED_PLAYER_IN_RAID")
    assert "SAVED_PLAYER_IN_RAID" in partner.long_term_traits
    assert partner.trust == 100
    partner.register_memory(10, "minor_event")
    partner.decay_memory(10)
    assert len(partner.short_term_memory) == 1


def test_pet_is_independent_from_partner():
    pet = PetEntity("pet1", "Wolf", current_hp=100, max_hp=100, energy=100, hunger=0)
    assert pet.choose_behavior(owner_distance=5) is PetBehavior.FOLLOW
    pet.energy = 0
    assert pet.choose_behavior(owner_distance=5) is PetBehavior.REST
