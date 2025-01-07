
function elementToJson(node) {
    const obj = {};
    if (node.nodeType === Node.ELEMENT_NODE) {
        obj[node.nodeName] = {};
        for (let childNode of node.childNodes) {
            const childObj = elementToJson(childNode);
            if (Object.keys(childObj).length > 0) {
                Object.assign(obj[node.nodeName], childObj);
            }
        }
        if (node.attributes.length > 0) {
            obj[node.nodeName]['_attributes'] = {};
            for (let attr of node.attributes) {
                obj[node.nodeName]['_attributes'][attr.name] = attr.value;
            }
        }
    } else if (node.nodeType === Node.TEXT_NODE) {
        obj = node.nodeValue.trim();
    }
    return obj;
}

let isInitialized = false;

function getNewPortDiv(prefix, deviceNo) {
    const newPortDiv = $("<div>");
    newPortDiv.addClass("port");

    for (let pinNo = 1; pinNo <= 16; ++pinNo) {
        const newDiv = $("<div>");
        newDiv.attr("id", prefix + "_port" + deviceNo + "_" + (16 - pinNo + 1));
        newDiv.addClass("pin");
        newDiv.addClass("pinDisabled");
        const absoluteAddr = ((deviceNo - 1) * 16) + (16 - pinNo + 1);
        newDiv.append(deviceNo + ":" + (16 - pinNo + 1) + "<br />" + absoluteAddr)
        newPortDiv.append(newDiv);
    }

    return newPortDiv;
}

function getNewEmptyPortDiv(prefix) {
    const newPortDiv = $("<div>");
    newPortDiv.addClass("portNone");
    newPortDiv.text("🏴 none");
    return newPortDiv;
}

function generateBaseView(jsonData) {

    if (isInitialized === true) return;
    isInitialized = true;

    let leftNo = jsonData.info.left;
    let middleNo = jsonData.info.middle;
    let rightNo = jsonData.info.right;

    if (leftNo === 0) {
        const newPortDiv = getNewEmptyPortDiv("left");
        $('#s88left').append(newPortDiv);
    } else {
        for (let deviceNo = 1; deviceNo <= leftNo; ++deviceNo) {
            const newPortDiv = getNewPortDiv("left", deviceNo);
            $('#s88left').append(newPortDiv);
        }
    }

    if (middleNo === 0) {
        const newPortDiv = getNewEmptyPortDiv("middle");
        $('#s88middle').append(newPortDiv);
    } else {
        for (let deviceNo = 1; deviceNo <= middleNo; ++deviceNo) {
            const newPortDiv = getNewPortDiv("middle", deviceNo);
            $('#s88middle').append(newPortDiv);
        }
    }

    if (rightNo === 0) {
        const newPortDiv = getNewEmptyPortDiv("right");
        $('#s88right').append(newPortDiv);
    } else {
        for (let deviceNo = 1; deviceNo <= rightNo; ++deviceNo) {
            const newPortDiv = getNewPortDiv("right", deviceNo);
            $('#s88right').append(newPortDiv);
        }
    }
}

function updateView(jsonData) {
    let portNo = jsonData.event.port;

    /*
    {
        "event": {
            "objectId": 100,
            "port": 1,
            "state": {
                "hex": "0FA3",
                "binary": "0000111110100011"
            }
        },
        "info": {
            "left": 5,
            "middle": 0,
            "right": 0
        }
    }
    */

	let evData = jsonData.event;
	let evInfo = jsonData.info;

	let portIdAccessor = '';

	if( portNo >= 0 && portNo <= evInfo.left ) {
		portIdAccessor = "left";
	} else if( portNo > evInfo.left && portNo <= (evInfo.left + evInfo.middle) ) { 
		portIdAccessor = "middle";
	} else if( portNo > (evInfo.left + evInfo.middle) ) {
		portIdAccessor = "right";
	}
	
	if(portIdAccessor.length > 0) {
		
		let portState = evData.state.binary;
				
		for (let pinNo = 1; pinNo <= 16; ++pinNo) {
			const idx = 16 - pinNo;
			const id = "#" + portIdAccessor + "_port" + portNo + "_" + pinNo;			
			const pinState = parseInt(portState[idx]);
			$(id).removeClass("pinDisabled");
			$(id).removeClass("pinEnabled");
			if (pinState === 1) {
				$(id).addClass("pinEnabled");
			} else {
				$(id).addClass("pinDisabled");
			}
		}					
		
	}
    
}

$(document).ready(function () {
    let socket = new WebSocket("ws://192.168.178.129:15472/s88/");
    socket.onclose = function () { console.log("Closed!"); };
    socket.onopen = function () { console.log("Connected!"); };
    socket.onmessage = function (msg) {
        let jsonData = JSON.parse(msg.data);
        generateBaseView(jsonData);
        updateView(jsonData);
    };
})