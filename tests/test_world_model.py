from app.simulation.world_model import MapNode, MonsterModel, SkillModel, WorldModel
from app.simulation.tactical import Element


def test_world_model_validates_references() -> None:
    model = WorldModel(
        maps={"m1": MapNode("m1", "Map", neighbors=("m2",), monsters=("mob",))},
        monsters={"mob": MonsterModel("mob", "Mob", 10, 100, 10, 5, skills=("hit",))},
        skills={"hit": SkillModel("hit", "Hit", damage=10, element=Element.FIRE)},
    )
    assert model.validate() == ("map:m1:unknown-neighbor:m2",)


def test_world_model_accepts_complete_references() -> None:
    model = WorldModel(
        maps={"m1": MapNode("m1", "Map", monsters=("mob",))},
        monsters={"mob": MonsterModel("mob", "Mob", 10, 100, 10, 5, skills=("hit",))},
        skills={"hit": SkillModel("hit", "Hit", damage=10)},
    )
    assert model.validate() == ()
