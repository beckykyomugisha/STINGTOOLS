"""Tests for stingtools_bonsai.core.category_inference."""
import pytest
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from core.category_inference import infer_disc_sys, infer_disc, infer_sys


class TestInferDiscSys:
    def test_duct_segment_uppercase(self):
        assert infer_disc_sys("IFCDUCTSEGMENT") == ("M", "HVAC")

    def test_duct_segment_mixed_case(self):
        assert infer_disc_sys("IfcDuctSegment") == ("M", "HVAC")

    def test_duct_segment_no_prefix(self):
        assert infer_disc_sys("DuctSegment") == ("M", "HVAC")

    def test_cable_segment(self):
        assert infer_disc_sys("IfcCableSegment") == ("E", "LV")

    def test_sanitary_terminal(self):
        assert infer_disc_sys("IfcSanitaryTerminal") == ("P", "SAN")

    def test_fire_suppression(self):
        assert infer_disc_sys("IfcFireSuppressionTerminal") == ("F", "FPS")

    def test_wall_architecture(self):
        assert infer_disc_sys("IfcWall") == ("A", "ARC")

    def test_beam_structure(self):
        assert infer_disc_sys("IfcBeam") == ("S", "STR")

    def test_boiler_hws(self):
        assert infer_disc_sys("IfcBoiler") == ("M", "HWS")

    def test_chiller_chw(self):
        assert infer_disc_sys("IfcChiller") == ("M", "CHW")

    def test_light_fixture(self):
        assert infer_disc_sys("IfcLightFixture") == ("E", "LTG")

    def test_transformer_hv(self):
        assert infer_disc_sys("IfcTransformer") == ("E", "HV")

    def test_sensor_bms(self):
        assert infer_disc_sys("IfcSensor") == ("M", "BMS")

    def test_unknown_returns_none_tuple(self):
        assert infer_disc_sys("IfcFoo") == (None, None)

    def test_empty_string_returns_none_tuple(self):
        assert infer_disc_sys("") == (None, None)


class TestInferDisc:
    def test_duct_disc(self):
        assert infer_disc("IfcDuctSegment") == "M"

    def test_unknown_disc(self):
        assert infer_disc("IfcUnknown") is None


class TestInferSys:
    def test_duct_sys(self):
        assert infer_sys("IfcDuctSegment") == "HVAC"

    def test_unknown_sys(self):
        assert infer_sys("IfcUnknown") is None
