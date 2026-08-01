from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def test_stage01_projects_initialization_fields_to_official_hifc_parameters():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs")
    for parameter in (
        "HIFC.申报信息属性集.项目编号",
        "HIFC.申报信息属性集.项目名称",
        "HIFC.申报信息属性集.项目地址",
        "HIFC.申报信息属性集.建设单位",
        "HIFC.申报信息属性集.设计单位",
        "HIFC.申报信息属性集.基点坐标X",
        "HIFC.申报信息属性集.基点坐标Y",
        "HIFC.申报信息属性集.基点高程",
        "HIFC.申报信息属性集.坐标系名称",
        "HIFC.申报信息属性集.高程系名称",
    ):
        assert parameter in service


def test_stage01_projection_installs_binds_writes_and_verifies_by_guid():
    service = read("src/BIMBaoGui.Stage01/Revit/Stage01OfficialHifcProjectionService.cs")
    assert "ParameterBindings.Insert" in service
    assert "ParameterBindings.ReInsert" in service
    assert "get_Parameter(item.Key.ParameterGuid)" in service
    assert "document.Regenerate()" in service
    assert "ReadbackMatches" in service
    assert "DUT_METERS" in service


def test_stage01_projection_runs_inside_existing_initialization_transaction():
    storage = read("src/BIMBaoGui.Stage01/Revit/Stage01Storage.cs")
    revit_service = read("src/BIMBaoGui.Stage01/Revit/Stage01RevitService.cs")
    assert "Stage01OfficialHifcProjectionService.WriteAndVerify" in storage
    assert "Stage01Storage.Write" in revit_service
    assert "using (var transaction = new Transaction" in revit_service
    assert "group.RollBack()" in revit_service
