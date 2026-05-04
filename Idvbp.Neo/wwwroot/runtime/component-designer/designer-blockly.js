(() => {
    const eventOptions = [
        ["设计器就绪 designer.ready", "designer.ready"],
        ["房间完整快照 room.snapshot", "room.snapshot"],
        ["房间信息更新 room.info.updated", "room.info.updated"],
        ["比赛创建 match.created", "match.created"],
        ["地图更新 room.map.updated", "room.map.updated"],
        ["本轮禁用更新 room.ban.updated", "room.ban.updated"],
        ["全局禁用更新 room.global-ban.updated", "room.global-ban.updated"],
        ["角色选择更新 room.role.selected", "room.role.selected"],
        ["求生者 1 选择 designer.survivor1.selected", "designer.survivor1.selected"],
        ["求生者 2 选择 designer.survivor2.selected", "designer.survivor2.selected"],
        ["求生者 3 选择 designer.survivor3.selected", "designer.survivor3.selected"],
        ["求生者 4 选择 designer.survivor4.selected", "designer.survivor4.selected"],
        ["监管者选择 designer.hunter.selected", "designer.hunter.selected"],
        ["阶段变化 room.phase.updated", "room.phase.updated"],
        ["进入禁用阶段 designer.phase.ban.enter", "designer.phase.ban.enter"],
        ["进入选择阶段 designer.phase.pick.enter", "designer.phase.pick.enter"],
        ["进入比分阶段 designer.phase.score.enter", "designer.phase.score.enter"],
        ["地图选定 designer.map.selected", "designer.map.selected"],
        ["前台重置 frontend.reset", "frontend.reset"],
        ["停止全部动画 frontend.animation.stopAll", "frontend.animation.stopAll"]
    ];

    function defineBlocks(elementOptions) {
        if (Blockly.Blocks.idvbp_event) {
            return;
        }

        defineEventBlocks();
        defineValueBlocks();
        defineLogicBlocks();
        defineMathBlocks();
        defineTextBlocks();
        defineJsonBlocks();
        defineActionBlocks(elementOptions);
        defineFlowAndApiBlocks();
    }

    function defineEventBlocks() {
        Blockly.Blocks.idvbp_event = {
            init() {
                this.appendDummyInput()
                    .appendField("当事件")
                    .appendField(new Blockly.FieldDropdown(eventOptions), "EVENT");
                this.appendStatementInput("DO").appendField("执行");
                this.setColour(210);
            }
        };

        Blockly.Blocks.idvbp_if = {
            init() {
                this.appendValueInput("CONDITION").setCheck("Boolean").appendField("如果");
                this.appendStatementInput("THEN").appendField("则执行");
                this.appendStatementInput("ELSE").appendField("否则执行");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(210);
            }
        };
    }

    function defineValueBlocks() {
        Blockly.Blocks.idvbp_value_text = {
            init() {
                this.appendDummyInput()
                    .appendField("文本")
                    .appendField(new Blockly.FieldTextInput("Ready"), "VALUE");
                this.setOutput(true, "String");
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_value_number = {
            init() {
                this.appendDummyInput()
                    .appendField("数字")
                    .appendField(new Blockly.FieldNumber(0), "VALUE");
                this.setOutput(true, "Number");
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_value_boolean = {
            init() {
                this.appendDummyInput()
                    .appendField("布尔")
                    .appendField(new Blockly.FieldDropdown([["真", "true"], ["假", "false"]]), "VALUE");
                this.setOutput(true, "Boolean");
                this.setColour(210);
            }
        };

        Blockly.Blocks.idvbp_value_event = {
            init() {
                this.appendDummyInput()
                    .appendField("事件数据")
                    .appendField(new Blockly.FieldTextInput("payload"), "PATH");
                this.setOutput(true);
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_value_room = {
            init() {
                this.appendDummyInput()
                    .appendField("房间数据")
                    .appendField(new Blockly.FieldTextInput("teamA.name"), "PATH");
                this.setOutput(true);
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_value_config = {
            init() {
                this.appendDummyInput()
                    .appendField("组件配置")
                    .appendField(new Blockly.FieldTextInput("title"), "PATH");
                this.setOutput(true);
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_value_variable = {
            init() {
                this.appendDummyInput()
                    .appendField("读取变量")
                    .appendField(new Blockly.FieldTextInput("apiResult"), "PATH");
                this.setOutput(true);
                this.setColour(290);
            }
        };

        Blockly.Blocks.idvbp_variable_exists = {
            init() {
                this.appendDummyInput()
                    .appendField("变量存在")
                    .appendField(new Blockly.FieldTextInput("apiResult"), "PATH");
                this.setOutput(true, "Boolean");
                this.setColour(290);
            }
        };

        Blockly.Blocks.idvbp_value_loop_item = {
            init() {
                this.appendDummyInput()
                    .appendField("循环项")
                    .appendField(new Blockly.FieldTextInput("name"), "PATH");
                this.setOutput(true);
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_value_api_response = {
            init() {
                this.appendDummyInput()
                    .appendField("API 返回数据")
                    .appendField(new Blockly.FieldTextInput("data"), "PATH");
                this.setOutput(true);
                this.setColour(25);
            }
        };

        Blockly.Blocks.idvbp_value_api_status = {
            init() {
                this.appendDummyInput().appendField("API 状态码");
                this.setOutput(true, "Number");
                this.setColour(25);
            }
        };

        Blockly.Blocks.idvbp_value_api_ok = {
            init() {
                this.appendDummyInput().appendField("API 是否成功");
                this.setOutput(true, "Boolean");
                this.setColour(25);
            }
        };
    }

    function defineLogicBlocks() {
        Blockly.Blocks.idvbp_compare = {
            init() {
                this.appendValueInput("LEFT");
                this.appendValueInput("RIGHT")
                    .appendField(new Blockly.FieldDropdown([
                        ["等于", "=="], ["不等于", "!="], ["大于", ">"], ["小于", "<"], ["大于等于", ">="], ["小于等于", "<="]
                    ]), "OP");
                this.setOutput(true, "Boolean");
                this.setInputsInline(true);
                this.setColour(210);
            }
        };

        Blockly.Blocks.idvbp_logic_op = {
            init() {
                this.appendValueInput("LEFT").setCheck("Boolean");
                this.appendValueInput("RIGHT")
                    .setCheck("Boolean")
                    .appendField(new Blockly.FieldDropdown([["并且", "AND"], ["或者", "OR"]]), "OP");
                this.setOutput(true, "Boolean");
                this.setInputsInline(true);
                this.setColour(210);
            }
        };

        Blockly.Blocks.idvbp_logic_not = {
            init() {
                this.appendValueInput("VALUE").setCheck("Boolean").appendField("非");
                this.setOutput(true, "Boolean");
                this.setColour(210);
            }
        };
    }

    function defineMathBlocks() {
        Blockly.Blocks.idvbp_arithmetic = {
            init() {
                this.appendValueInput("LEFT").setCheck("Number");
                this.appendValueInput("RIGHT")
                    .setCheck("Number")
                    .appendField(new Blockly.FieldDropdown([
                        ["加", "+"], ["减", "-"], ["乘", "*"], ["除", "/"], ["取余", "%"]
                    ]), "OP");
                this.setOutput(true, "Number");
                this.setInputsInline(true);
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_math_single = {
            init() {
                this.appendValueInput("VALUE").setCheck("Number")
                    .appendField(new Blockly.FieldDropdown([
                        ["绝对值", "abs"], ["四舍五入", "round"], ["向下取整", "floor"],
                        ["向上取整", "ceil"], ["平方根", "sqrt"], ["随机整数 0 到 N", "random"]
                    ]), "OP");
                this.setOutput(true, "Number");
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_to_number = {
            init() {
                this.appendValueInput("VALUE").appendField("转为数字");
                this.setOutput(true, "Number");
                this.setColour(230);
            }
        };
    }

    function defineTextBlocks() {
        Blockly.Blocks.idvbp_text_join = {
            init() {
                this.appendValueInput("A").appendField("拼接文本");
                this.appendValueInput("B").appendField("和");
                this.setOutput(true, "String");
                this.setInputsInline(true);
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_text_length = {
            init() {
                this.appendValueInput("VALUE").appendField("文本长度");
                this.setOutput(true, "Number");
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_text_contains = {
            init() {
                this.appendValueInput("TEXT").appendField("文本");
                this.appendValueInput("SEARCH").appendField("包含");
                this.setOutput(true, "Boolean");
                this.setInputsInline(true);
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_text_replace = {
            init() {
                this.appendValueInput("TEXT").appendField("文本");
                this.appendValueInput("FROM").appendField("替换");
                this.appendValueInput("TO").appendField("为");
                this.setOutput(true, "String");
                this.setInputsInline(true);
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_text_substring = {
            init() {
                this.appendValueInput("TEXT").appendField("截取文本");
                this.appendValueInput("START").setCheck("Number").appendField("从");
                this.appendValueInput("END").setCheck("Number").appendField("到");
                this.setOutput(true, "String");
                this.setInputsInline(true);
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_text_case = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField(new Blockly.FieldDropdown([["转大写", "toUpperCase"], ["转小写", "toLowerCase"]]), "OP");
                this.setOutput(true, "String");
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_to_string = {
            init() {
                this.appendValueInput("VALUE").appendField("转为文本");
                this.setOutput(true, "String");
                this.setColour(160);
            }
        };
    }

    function defineJsonBlocks() {
        Blockly.Blocks.idvbp_json_parse = {
            init() {
                this.appendValueInput("VALUE").appendField("解析 JSON");
                this.setOutput(true, "Object");
                this.setColour(260);
            }
        };

        Blockly.Blocks.idvbp_json_get = {
            init() {
                this.appendValueInput("OBJECT").appendField("对象")
                    .appendField(new Blockly.FieldTextInput("key"), "KEY")
                    .appendField("属性");
                this.setOutput(true);
                this.setInputsInline(true);
                this.setColour(260);
            }
        };

        Blockly.Blocks.idvbp_json_path = {
            init() {
                this.appendValueInput("OBJECT").appendField("取对象路径");
                this.appendDummyInput()
                    .appendField(new Blockly.FieldTextInput("data.0.name"), "KEY");
                this.setOutput(true);
                this.setInputsInline(true);
                this.setColour(260);
            }
        };

        Blockly.Blocks.idvbp_json_stringify = {
            init() {
                this.appendValueInput("VALUE").appendField("序列化 JSON");
                this.setOutput(true, "String");
                this.setColour(260);
            }
        };

        Blockly.Blocks.idvbp_json_has = {
            init() {
                this.appendValueInput("OBJECT").appendField("对象包含属性")
                    .appendField(new Blockly.FieldTextInput("key"), "KEY");
                this.setOutput(true, "Boolean");
                this.setInputsInline(true);
                this.setColour(260);
            }
        };

        Blockly.Blocks.idvbp_typeof = {
            init() {
                this.appendValueInput("VALUE").appendField("类型判断");
                this.setOutput(true, "String");
                this.setColour(260);
            }
        };
    }

    function defineActionBlocks(elementOptions) {
        Blockly.Blocks.idvbp_pulse = {
            init() {
                this.appendDummyInput()
                    .appendField("元素强调动画")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(45);
            }
        };

        Blockly.Blocks.idvbp_set_text = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("文字为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(120);
            }
        };

        Blockly.Blocks.idvbp_set_visible = {
            init() {
                this.appendDummyInput()
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("可见性")
                    .appendField(new Blockly.FieldDropdown([["显示", "true"], ["隐藏", "false"]]), "VALUE");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(120);
            }
        };

        Blockly.Blocks.idvbp_set_source = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("资源为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(120);
            }
        };

        Blockly.Blocks.idvbp_move_element = {
            init() {
                this.appendDummyInput()
                    .appendField("移动元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("到 X")
                    .appendField(new Blockly.FieldNumber(0), "LEFT")
                    .appendField("Y")
                    .appendField(new Blockly.FieldNumber(0), "TOP");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(65);
            }
        };

        Blockly.Blocks.idvbp_resize_element = {
            init() {
                this.appendDummyInput()
                    .appendField("调整元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("宽")
                    .appendField(new Blockly.FieldNumber(100), "WIDTH")
                    .appendField("高")
                    .appendField(new Blockly.FieldNumber(40), "HEIGHT");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(65);
            }
        };

        Blockly.Blocks.idvbp_set_style = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("样式")
                    .appendField(new Blockly.FieldDropdown([
                        ["文字色", "color"],
                        ["字号", "fontSize"],
                        ["背景", "background"],
                        ["透明度", "opacity"]
                    ]), "PROP")
                    .appendField("为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(65);
            }
        };

        Blockly.Blocks.idvbp_play_animation = {
            init() {
                this.appendDummyInput()
                    .appendField("播放动画")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField(new Blockly.FieldTextInput("pulse 520ms ease"), "ANIMATION");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(45);
            }
        };

        Blockly.Blocks.idvbp_set_config = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("写入配置")
                    .appendField(new Blockly.FieldTextInput("key"), "KEY")
                    .appendField("为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(290);
            }
        };

        Blockly.Blocks.idvbp_set_variable = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("设置变量")
                    .appendField(new Blockly.FieldTextInput("value"), "KEY")
                    .appendField("为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(290);
            }
        };

        Blockly.Blocks.idvbp_emit = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("发出事件")
                    .appendField(new Blockly.FieldTextInput("custom.event"), "TYPE")
                    .appendField("数据");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(20);
            }
        };
    }

    function defineFlowAndApiBlocks() {
        Blockly.Blocks.idvbp_set_timeout = {
            init() {
                this.appendValueInput("DELAY").setCheck("Number").appendField("延迟");
                this.appendStatementInput("DO").appendField("毫秒后执行");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(40);
            }
        };

        Blockly.Blocks.idvbp_set_interval = {
            init() {
                this.appendValueInput("INTERVAL").setCheck("Number").appendField("每隔");
                this.appendStatementInput("DO").appendField("毫秒重复执行");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(40);
            }
        };

        Blockly.Blocks.idvbp_repeat_times = {
            init() {
                this.appendValueInput("TIMES").setCheck("Number").appendField("循环");
                this.appendStatementInput("DO").appendField("次执行");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(40);
            }
        };

        Blockly.Blocks.idvbp_for_each = {
            init() {
                this.appendValueInput("LIST").appendField("遍历列表");
                this.appendDummyInput()
                    .appendField("当前项名")
                    .appendField(new Blockly.FieldTextInput("item"), "ITEM");
                this.appendStatementInput("DO").appendField("执行");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(40);
            }
        };

        Blockly.Blocks.idvbp_console_log = {
            init() {
                this.appendValueInput("VALUE").appendField("输出日志");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(40);
            }
        };

        Blockly.Blocks.idvbp_fetch_url = {
            init() {
                this.appendValueInput("URL").appendField("请求 URL")
                    .appendField(new Blockly.FieldDropdown([["GET", "GET"], ["POST", "POST"], ["PATCH", "PATCH"], ["PUT", "PUT"], ["DELETE", "DELETE"]]), "METHOD");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(40);
            }
        };

        Blockly.Blocks.idvbp_api_request = {
            init() {
                this.appendDummyInput()
                    .appendField("请求后端 API")
                    .appendField(new Blockly.FieldDropdown([["GET", "GET"], ["POST", "POST"], ["PATCH", "PATCH"], ["PUT", "PUT"], ["DELETE", "DELETE"]]), "METHOD")
                    .appendField(new Blockly.FieldTextInput("/api/rooms"), "URL");
                this.setOutput(true);
                this.setColour(25);
            }
        };

        Blockly.Blocks.idvbp_api_request_body = {
            init() {
                this.appendDummyInput()
                    .appendField("请求后端 API")
                    .appendField(new Blockly.FieldDropdown([["POST", "POST"], ["PATCH", "PATCH"], ["PUT", "PUT"], ["DELETE", "DELETE"], ["GET", "GET"]]), "METHOD")
                    .appendField(new Blockly.FieldTextInput("/api/rooms"), "URL");
                this.appendValueInput("BODY").appendField("JSON 请求体");
                this.setOutput(true);
                this.setColour(25);
            }
        };
    }

    function buildToolbox() {
        return {
            kind: "categoryToolbox",
            contents: [
                category("事件", "210", ["idvbp_event", "idvbp_if"]),
                category("逻辑", "210", ["idvbp_compare", "idvbp_logic_op", "idvbp_logic_not", "idvbp_value_boolean"]),
                category("取数据", "230", ["idvbp_value_text", "idvbp_value_number", "idvbp_value_event", "idvbp_value_room", "idvbp_value_config", "idvbp_value_variable", "idvbp_variable_exists", "idvbp_value_loop_item"]),
                category("数字", "230", ["idvbp_arithmetic", "idvbp_math_single", "idvbp_to_number"]),
                category("文本", "160", ["idvbp_text_join", "idvbp_text_length", "idvbp_text_contains", "idvbp_text_replace", "idvbp_text_substring", "idvbp_text_case", "idvbp_to_string"]),
                category("JSON", "260", ["idvbp_json_path", "idvbp_json_parse", "idvbp_json_get", "idvbp_json_stringify", "idvbp_json_has", "idvbp_typeof"]),
                category("元素操作", "120", ["idvbp_set_text", "idvbp_set_visible", "idvbp_set_source", "idvbp_move_element", "idvbp_resize_element", "idvbp_set_style", "idvbp_pulse", "idvbp_play_animation", "idvbp_set_variable", "idvbp_set_config", "idvbp_emit"]),
                category("流程", "40", ["idvbp_set_timeout", "idvbp_set_interval", "idvbp_repeat_times", "idvbp_for_each", "idvbp_console_log"]),
                category("后端 API", "25", ["idvbp_api_request", "idvbp_api_request_body"])
            ]
        };
    }

    function category(name, colour, blockTypes) {
        return {
            kind: "category",
            name,
            colour,
            contents: blockTypes.map(type => ({ kind: "block", type }))
        };
    }

    function loadDefaultBlocks(workspace) {
        const xml = Blockly.utils.xml.textToDom(`
            <xml xmlns="https://developers.google.com/blockly/xml">
                <block type="idvbp_event" x="24" y="28">
                    <field name="EVENT">room.snapshot</field>
                    <statement name="DO">
                        <block type="idvbp_set_text">
                            <field name="TARGET">title</field>
                            <value name="VALUE">
                                <block type="idvbp_value_room">
                                    <field name="PATH">roomName</field>
                                </block>
                            </value>
                            <next>
                                <block type="idvbp_pulse">
                                    <field name="TARGET">title</field>
                                </block>
                            </next>
                        </block>
                    </statement>
                </block>
            </xml>`);
        Blockly.Xml.domToWorkspace(xml, workspace);
    }

    window.IdvbpDesignerBlockly = {
        eventOptions,
        defineBlocks,
        buildToolbox,
        loadDefaultBlocks
    };
})();
